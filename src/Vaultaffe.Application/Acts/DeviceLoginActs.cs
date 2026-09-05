using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Application.Acts;

/// <summary>What the CLI is told when a login begins.</summary>
/// <param name="DeviceCode">The long code the CLI keeps and polls with. Never shown to a person.</param>
/// <param name="UserCode">The short code the CLI prints, as a person reads it.</param>
/// <param name="VerificationUri">Where a human goes to confirm.</param>
/// <param name="VerificationUriComplete">The same, with the code already in it.</param>
/// <param name="ExpiresInSeconds">How long the human has.</param>
/// <param name="IntervalSeconds">How often the CLI should poll.</param>
public sealed record DeviceLoginBegun(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int ExpiresInSeconds,
    int IntervalSeconds);

/// <summary>
/// Begin a device-code login (Specification §6.2). Unauthenticated, because the
/// whole point is that the machine asking has nothing yet.
/// </summary>
public sealed class BeginDeviceLogin(IIdentityStore identities, TimeProvider clock)
{
    /// <summary>The page a human is sent to. Relative; the CLI has the host already.</summary>
    public const string VerificationPath = "/device";

    public async Task<DeviceLoginBegun> ExecuteAsync(CancellationToken cancellationToken)
    {
        // The MVP has exactly one organization (§5) and a login that has not
        // happened yet cannot name one. When there are several, what resolves it
        // is the human who confirms — and the column this goes into is where that
        // answer already belongs.
        var organization = await identities.FindTheOrganizationAsync(cancellationToken)
            ?? throw new Refusal(
                RefusalCode.NotFound,
                "This instance has not been started yet. Its first user is created before "
                + "anybody can sign in.");

        var (authorization, deviceCode, userCode) = DeviceAuthorization.Begin(
            Guid.NewGuid(), organization.Id, clock.GetUtcNow());

        await identities.AddDeviceAuthorizationAsync(authorization, cancellationToken);

        var readable = UserCode.ForReading(userCode);

        return new DeviceLoginBegun(
            deviceCode,
            readable,
            VerificationPath,
            $"{VerificationPath}?code={readable}",
            (int)DeviceAuthorization.Lifetime.TotalSeconds,
            (int)DeviceAuthorization.PollingInterval.TotalSeconds);
    }
}

/// <summary>
/// A human confirms or refuses a login, having signed in on the confirmation page
/// itself.
/// </summary>
/// <remarks>
/// The password is checked here rather than a session being assumed, because this
/// page is the whole of the browser surface in this stage (Specification §6.6):
/// the management UI, and whatever it signs a person in with, arrives in the next
/// one. A page that asked for nothing would be a page anybody who guessed a
/// user code could use.
/// </remarks>
public sealed class ConfirmDeviceLogin(
    IIdentityStore identities,
    IPasswordHasher passwords,
    TimeProvider clock)
{
    /// <summary>Confirm it. The user code is what a person typed, in any spelling.</summary>
    public Task<string> ApproveAsync(
        string typedCode, string email, string password, CancellationToken cancellationToken) =>
        DecideAsync(typedCode, email, password, approve: true, cancellationToken);

    /// <summary>Refuse it — the human did not start this login.</summary>
    public Task<string> DenyAsync(
        string typedCode, string email, string password, CancellationToken cancellationToken) =>
        DecideAsync(typedCode, email, password, approve: false, cancellationToken);

    private async Task<string> DecideAsync(
        string typedCode,
        string email,
        string password,
        bool approve,
        CancellationToken cancellationToken)
    {
        var code = UserCode.Normalize(typedCode);

        var user = await identities.FindUserByEmailAsync(
            EmailAddress.Normalize(email), cancellationToken);

        var correct = await passwords.VerifyAsync(
            user?.PasswordHash ?? passwords.DecoyHash, password ?? string.Empty, cancellationToken);

        if (user is null || !correct)
        {
            throw Refusal.Unauthenticated("That email address and password do not match.");
        }

        var authorization = code.Length == 0
            ? null
            : await identities.FindDeviceAuthorizationByUserCodeAsync(code, cancellationToken);

        if (authorization is null)
        {
            throw Refusal.NotFound("No login is waiting for that code.");
        }

        var now = clock.GetUtcNow();

        var decided = approve
            ? authorization.ApproveBy(user.Id, now)
            : authorization.Deny(now);

        if (!decided)
        {
            throw new Refusal(
                authorization.StateAt(now) is DeviceAuthorizationState.Denied
                    ? RefusalCode.DeviceDenied
                    : RefusalCode.DeviceExpired,
                "That login is no longer waiting to be confirmed.");
        }

        await identities.SaveAsync(cancellationToken);

        return user.Name;
    }
}

/// <summary>
/// The CLI's poll: the session token once a human has confirmed, and a code that
/// says why not until then.
/// </summary>
public sealed class RedeemDeviceLogin(IIdentityStore identities, TimeProvider clock)
{
    public async Task<SignedIn> ExecuteAsync(string? deviceCode, CancellationToken cancellationToken)
    {
        var found = string.IsNullOrWhiteSpace(deviceCode)
            ? null
            : await identities.FindDeviceAuthorizationByCodeHashAsync(
                DeviceCode.Hash(deviceCode), cancellationToken);

        if (found is null)
        {
            throw Refusal.NotFound("No login is waiting for that device code.");
        }

        var (authorization, approvedBy) = found.Value;
        var now = clock.GetUtcNow();

        // Every state but one ends the polling, and each is its own code so that
        // a CLI keeps waiting on exactly one of them (docs/api.md).
        switch (authorization.StateAt(now))
        {
            case DeviceAuthorizationState.Pending:
                throw new Refusal(
                    RefusalCode.DevicePending, "Nobody has confirmed this login yet.");
            case DeviceAuthorizationState.Denied:
                throw new Refusal(
                    RefusalCode.DeviceDenied, "A human refused this login.");
            case DeviceAuthorizationState.Expired:
                throw new Refusal(
                    RefusalCode.DeviceExpired, "This login expired before it was confirmed.");
            case DeviceAuthorizationState.Redeemed:
                throw new Refusal(
                    RefusalCode.DeviceExpired,
                    "This login's token has already been collected. A device code works once.");
        }

        var user = approvedBy
            ?? throw new Refusal(
                RefusalCode.NotFound, "The user who confirmed this login is no longer here.");

        var expiresAt = now + Sessions.Lifetime;

        var (token, value) = Token.Issue(
            Guid.NewGuid(),
            user.OrganizationId,
            user.Id,
            TokenKind.Session,
            name: null,
            Scopes.Everything,
            now,
            expiresAt);

        // Redeeming and issuing are one step: a device code that stayed usable
        // after handing a token over would be a second session for whoever still
        // has it.
        authorization.RedeemTo(token.Id, now);

        await identities.AddTokenAsync(token, cancellationToken);

        return new SignedIn(
            user.Id, user.Name, user.Email, user.IsAdministrator, value.Reveal(), expiresAt);
    }
}
