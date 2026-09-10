using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Application.Acts;

/// <summary>What the asking machine is told when an enrollment begins.</summary>
/// <param name="DeviceCode">The long code the client keeps and polls with. Never shown to a person.</param>
/// <param name="UserCode">The short code the client prints, as a person reads it.</param>
/// <param name="VerificationUri">Where a person goes to decide.</param>
/// <param name="VerificationUriComplete">The same, with the code already in it.</param>
/// <param name="ExpiresInSeconds">How long the person has.</param>
/// <param name="IntervalSeconds">How often the client should poll.</param>
public sealed record EnrollmentBegun(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int ExpiresInSeconds,
    int IntervalSeconds);

/// <summary>What a person is shown when they open one, before they decide.</summary>
/// <param name="RequestedName">What the asking client calls itself. Untrusted, and the screen says so.</param>
/// <param name="AskedAt">When it asked.</param>
/// <param name="ExpiresAt">When the code stops being worth anything.</param>
/// <param name="State">Where it has got to, in case somebody opens a code twice.</param>
public sealed record EnrollmentAsked(
    string RequestedName,
    DateTimeOffset AskedAt,
    DateTimeOffset ExpiresAt,
    DeviceAuthorizationState State);

/// <summary>
/// A machine asks for a token of its own (Specification §6.2, §6.4). The
/// device-code flow of <see cref="BeginDeviceLogin"/>, ending in an agent token
/// rather than in a person's session
/// (<c>docs/adr/0021-an-agent-asks-for-its-own-token.md</c>).
/// </summary>
/// <remarks>
/// Unauthenticated, exactly as beginning a login is: the whole point is that the
/// machine asking has nothing yet. What stands in for authentication is that a
/// person has to open the code and agree to it within ten minutes, under their
/// own session — and that person is who the token is accountable to afterwards.
/// <para>
/// <b>What this replaces is a copy and a paste.</b> Creating a token prints its
/// value, and a value that has been printed has been in a terminal, a scrollback
/// and — where an agent ran the command — a transcript. Here the value is never
/// printed at all: it is answered to the poll and written to a file the client
/// chose, mode 0600. Nothing in the exchange is worth anything to whoever reads
/// it over the agent's shoulder: the short code needs a person's session to be
/// worth anything, and the long one never leaves the process that made it.
/// </para>
/// </remarks>
public sealed class BeginEnrollment(IIdentityStore identities, TimeProvider clock)
{
    /// <summary>The screen a person is sent to. Relative; the client has the host.</summary>
    public const string VerificationPath = "/enroll";

    /// <summary>The longest a client's name for itself may be.</summary>
    public const int NameLimit = CreateToken.NameLimit;

    public async Task<EnrollmentBegun> ExecuteAsync(
        string? requestedName, CancellationToken cancellationToken)
    {
        // The request is read before the instance is: what arrived is malformed
        // or it is not, and that answer does not depend on whether anybody has
        // started this installation yet.
        var named = (requestedName ?? string.Empty).Trim();

        if (named.Length is 0 or > NameLimit)
        {
            throw Refusal.Validation(
                "name",
                $"An enrollment needs a name of at most {NameLimit} characters: it is what the "
                + "person deciding sees, and what a revocation list will call this months from "
                + "now.");
        }

        var organization = await identities.FindTheOrganizationAsync(cancellationToken)
            ?? throw new Refusal(
                RefusalCode.NotFound,
                "This instance has not been started yet. Its first user is created before "
                + "anybody can be given a token.");

        var (authorization, deviceCode, userCode) = DeviceAuthorization.BeginEnrollment(
            Guid.NewGuid(), organization.Id, named, clock.GetUtcNow());

        await identities.AddDeviceAuthorizationAsync(authorization, cancellationToken);

        var readable = UserCode.ForReading(userCode);

        return new EnrollmentBegun(
            deviceCode,
            readable,
            VerificationPath,
            $"{VerificationPath}?code={readable}",
            (int)DeviceAuthorization.Lifetime.TotalSeconds,
            (int)DeviceAuthorization.PollingInterval.TotalSeconds);
    }
}

/// <summary>
/// What a person is being asked to agree to, by the short code they typed.
/// </summary>
/// <remarks>
/// A session is required, unlike the device-login page, which asks for a
/// password because it is the whole of the browser surface at the stage it was
/// written for (ADR 0008). This is a screen of the management application, and
/// the person reading it is signed in already.
/// <para>
/// It answers what the client said about itself and nothing else, because there
/// is nothing else to answer honestly: an instance cannot tell whether a machine
/// calling itself an agent on somebody's laptop is one. The screen says that in
/// as many words rather than dressing the claim up as a fact.
/// </para>
/// </remarks>
public sealed class ReadEnrollment(
    IIdentityStore identities, ICallerIdentity caller, TimeProvider clock)
{
    public async Task<EnrollmentAsked> ExecuteAsync(
        string typedCode, CancellationToken cancellationToken)
    {
        var authorization = await Enrollments.FoundAsync(
            identities, caller.Required, typedCode, cancellationToken);

        return new EnrollmentAsked(
            authorization.RequestedName ?? string.Empty,
            authorization.CreatedAt,
            authorization.ExpiresAt,
            authorization.StateAt(clock.GetUtcNow()));
    }
}

/// <summary>
/// A person agrees to it, and says what the token may do and how far it reaches.
/// </summary>
/// <remarks>
/// <b>Human-only</b>, declared by the endpoint as <c>CreateToken</c>, and it is
/// the same rule rather than a new one: what happens at the end of this is a
/// token being created, and one an agent could agree to for itself would be a
/// credential nobody issued.
/// <para>
/// The token is not created here. Its value exists exactly once, in the answer
/// that makes it, and the machine that asked is not on this request — so what an
/// approval records is the shape a person agreed to, and the collection builds
/// the token out of it.
/// </para>
/// </remarks>
public sealed class ApproveEnrollment(
    IIdentityStore identities, ICallerIdentity caller, TimeProvider clock)
{
    public async Task<EnrollmentAsked> ExecuteAsync(
        string typedCode,
        string? name,
        Scopes? scopes,
        IReadOnlyList<BindingRequest>? bindings,
        CancellationToken cancellationToken)
    {
        var acting = caller.Required;

        var authorization = await Enrollments.FoundAsync(
            identities, acting, typedCode, cancellationToken);

        var named = (name ?? authorization.RequestedName ?? string.Empty).Trim();

        if (named.Length is 0 or > CreateToken.NameLimit)
        {
            throw Refusal.Validation(
                "name",
                $"A token needs a name of at most {CreateToken.NameLimit} characters, so that a "
                + "revocation list is readable.");
        }

        var now = clock.GetUtcNow();

        // The default of an agent token is everything, because the point is
        // attribution and not restriction (§6.4). A person narrowing it here is
        // the whole reason this screen asks rather than just confirming.
        if (!authorization.ApproveBy(
            acting.UserId, named, scopes ?? CreateToken.DefaultScopesOf(TokenKind.Agent), now))
        {
            throw Enrollments.NoLongerWaiting(authorization, now);
        }

        foreach (var binding in bindings ?? [])
        {
            identities.Add(authorization.BindApprovalTo(
                Guid.NewGuid(), binding.ProjectId, binding.EnvironmentId));
        }

        await identities.SaveAsync(cancellationToken);

        return new EnrollmentAsked(
            named, authorization.CreatedAt, authorization.ExpiresAt, authorization.StateAt(now));
    }
}

/// <summary>A person says no: nothing on this machine asked for a token.</summary>
public sealed class RefuseEnrollment(
    IIdentityStore identities, ICallerIdentity caller, TimeProvider clock)
{
    public async Task<EnrollmentAsked> ExecuteAsync(
        string typedCode, CancellationToken cancellationToken)
    {
        var authorization = await Enrollments.FoundAsync(
            identities, caller.Required, typedCode, cancellationToken);

        var now = clock.GetUtcNow();

        if (!authorization.Deny(now))
        {
            throw Enrollments.NoLongerWaiting(authorization, now);
        }

        await identities.SaveAsync(cancellationToken);

        return new EnrollmentAsked(
            authorization.RequestedName ?? string.Empty,
            authorization.CreatedAt,
            authorization.ExpiresAt,
            authorization.StateAt(now));
    }
}

/// <summary>
/// The asking machine's poll: the token once a person has agreed, and a code
/// that says why not until then.
/// </summary>
/// <remarks>
/// The token is built here, from the shape the approval recorded, and its value
/// is in this answer and nowhere else — the same rule creation has followed since
/// there were tokens. It is accountable to the person who agreed to it, which is
/// what makes the change log able to say a human handed a machine a key
/// (§6.4, §6.5).
/// </remarks>
public sealed class CollectEnrollment(
    IIdentityStore identities, ChangeLog log, TimeProvider clock)
{
    public async Task<TokenIssued> ExecuteAsync(
        string? deviceCode, CancellationToken cancellationToken)
    {
        var found = string.IsNullOrWhiteSpace(deviceCode)
            ? null
            : await identities.FindDeviceAuthorizationByCodeHashAsync(
                DeviceCode.Hash(deviceCode), cancellationToken);

        if (found is null)
        {
            throw Refusal.NotFound("No enrollment is waiting for that device code.");
        }

        var (authorization, approvedBy) = found.Value;

        // A device code belongs to the flow it was issued for. Polling a login
        // here, or an enrollment at the login's endpoint, is a client using the
        // wrong half of the contract rather than a state of the request.
        if (authorization.Produces is not Handover.AgentToken)
        {
            throw Refusal.NotFound("No enrollment is waiting for that device code.");
        }

        var now = clock.GetUtcNow();

        switch (authorization.StateAt(now))
        {
            case DeviceAuthorizationState.Pending:
                throw new Refusal(
                    RefusalCode.DevicePending, "Nobody has agreed to this enrollment yet.");
            case DeviceAuthorizationState.Denied:
                throw new Refusal(
                    RefusalCode.DeviceDenied, "A person refused this enrollment.");
            case DeviceAuthorizationState.Expired:
                throw new Refusal(
                    RefusalCode.DeviceExpired,
                    "This enrollment expired before anybody agreed to it.");
            case DeviceAuthorizationState.Redeemed:
                throw new Refusal(
                    RefusalCode.DeviceExpired,
                    "This enrollment's token has already been collected. A device code works "
                    + "once.");
        }

        var person = approvedBy
            ?? throw new Refusal(
                RefusalCode.NotFound, "The person who agreed to this enrollment is no longer here.");

        var (token, value) = Token.Issue(
            Guid.NewGuid(),
            person.OrganizationId,
            person.Id,
            TokenKind.Agent,
            authorization.ApprovedName ?? authorization.RequestedName,
            authorization.ApprovedScopes ?? CreateToken.DefaultScopesOf(TokenKind.Agent),
            now);

        foreach (var agreed in authorization.ApprovedBindings)
        {
            token.BindTo(Guid.NewGuid(), agreed.ProjectId, agreed.EnvironmentId);
        }

        // Collecting and issuing are one step, for the reason a login's are: a
        // device code that stayed usable after handing a token over would be a
        // second credential for whoever still has it.
        authorization.RedeemTo(token.Id, now);

        // The same entry creating one writes, because it is the same act — a key
        // to the vault handed to a machine, under the name and by the person who
        // agreed to it. Never the value (§6.5, ADR 0020).
        log.RecordByPerson(person, ChangeAction.TokenCreated, about: token.Name);

        await identities.AddTokenAsync(token, cancellationToken);

        return new TokenIssued(CreateToken.Row(token), value.Reveal());
    }
}

/// <summary>What the four acts above share: finding one, and saying it is over.</summary>
internal static class Enrollments
{
    public static async Task<DeviceAuthorization> FoundAsync(
        IIdentityStore identities,
        Caller acting,
        string typedCode,
        CancellationToken cancellationToken)
    {
        var code = UserCode.Normalize(typedCode);

        var authorization = code.Length == 0
            ? null
            : await identities.FindDeviceAuthorizationByUserCodeAsync(code, cancellationToken);

        // Three ways to be nothing, and one answer to all of them. A login found
        // by this code is not an enrollment; one begun in another organization is
        // not this person's to decide — the store reaches past the filter here
        // because a poll carries no token, so the filtering this request could
        // have had is done in as many words. Saying which of the three it was
        // would be telling whoever typed a code which codes exist.
        if (authorization is null
            || authorization.Produces is not Handover.AgentToken
            || authorization.OrganizationId != acting.OrganizationId)
        {
            throw Refusal.NotFound("Nothing is waiting for that code.");
        }

        return authorization;
    }

    public static Refusal NoLongerWaiting(
        DeviceAuthorization authorization, DateTimeOffset now) =>
        new(
            authorization.StateAt(now) is DeviceAuthorizationState.Denied
                ? RefusalCode.DeviceDenied
                : RefusalCode.DeviceExpired,
            "That enrollment is no longer waiting to be decided.");
}
