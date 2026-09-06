using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Application.Acts;

/// <summary>
/// What a first run produces: the organization it made, and the session of the
/// person who made it — carrying the token, shown exactly once.
/// </summary>
public sealed record InstanceStarted(
    Guid OrganizationId, string OrganizationName, SignedIn Session);

/// <summary>
/// The first run (Specification §6.3): the default organization is created and
/// the first user becomes its administrator.
/// </summary>
/// <remarks>
/// It happens exactly once, and it authenticates nobody — there is nobody to
/// authenticate as yet. What it does require is <b>this instance's claim
/// secret</b>: the instance makes one for itself before it serves anything and
/// prints it in its log for as long as it is unclaimed, so that the operator
/// reading that log can start it and a stranger who merely reaches the port
/// cannot (ADR 0019).
/// <para>
/// That reverses <see href="../../../docs/adr/0007-the-first-run-is-unauthenticated-and-happens-once.md">ADR
/// 0007</see>, which left the window between `docker compose up` and the first
/// sign-in open on purpose and named it rather than closing it. What changed is
/// not the risk but the price: 0007 turned down a bootstrap secret an operator
/// had to put in a `.env` and would skip by copying an example, and this is not
/// that — nobody generates it, nobody stores it, and nobody can forget it.
/// </para>
/// <para>
/// The order of the two refusals matters. An instance that has already been
/// started says so <b>before</b> the claim secret is looked at, so that a
/// running instance never answers the question of whether a guess was right.
/// </para>
/// </remarks>
public sealed class StartTheInstance(
    IIdentityStore identities,
    IPasswordHasher passwords,
    ChangeLog log,
    TimeProvider clock)
{
    /// <summary>The name the MVP's one organization gets (Specification §6.1).</summary>
    public const string DefaultOrganizationName = "Default";

    public async Task<InstanceStarted> ExecuteAsync(
        string email,
        string name,
        string password,
        string? claimSecret,
        CancellationToken cancellationToken)
    {
        if (await identities.FindTheOrganizationAsync(cancellationToken) is not null)
        {
            throw new Refusal(
                RefusalCode.AlreadyStarted,
                "This instance has already been started. Sign in, or ask an administrator "
                + "to add you.");
        }

        // An unstarted instance always has one: it is written before anything is
        // served. Finding none means somebody deleted the row out from under a
        // running instance, and answering "your secret is wrong" to that would
        // send an operator looking for a secret rather than for the row.
        var claim = await identities.FindTheClaimAsync(cancellationToken)
            ?? throw new Refusal(
                RefusalCode.ClaimRefused,
                "This instance has no claim secret to check against. Restart it and it will "
                + "write a new one to its log.");

        if (!claim.Matches(claimSecret))
        {
            throw new Refusal(
                RefusalCode.ClaimRefused,
                "The first run needs this instance's claim secret. It is in the instance's "
                + "own log, printed at every start until somebody claims it — "
                + "`docker compose logs vaultaffe` on the machine it runs on, or "
                + "`deploy/claim.sh`.");
        }

        var address = Validated(email, name, password);
        var now = clock.GetUtcNow();

        var organization = new Organization(Guid.NewGuid(), DefaultOrganizationName, now);

        var user = new User(
            Guid.NewGuid(),
            organization.Id,
            address,
            name,
            await passwords.HashAsync(password, cancellationToken),
            isAdministrator: true,
            now);

        // A session token, because the first thing anybody does after starting an
        // instance is act in it — and the alternative is to sign in immediately
        // with the password that was just typed, for nothing.
        var expiresAt = now + Sessions.Lifetime;

        var (token, value) = Token.Issue(
            Guid.NewGuid(),
            organization.Id,
            user.Id,
            TokenKind.Session,
            name: null,
            Scopes.Everything,
            now,
            expiresAt);

        // The first entry this instance will ever have, recorded under the
        // identity this act just made: there is no caller, and the person who
        // claimed the instance is who acted. Every later entry about them leans
        // on it — an address change records the new address because the old one
        // is in the entry before it (ADR 0020).
        log.RecordBy(Caller.Of(user, token), ChangeAction.Joined, about: user.Email);

        await identities.StartTheInstanceAsync(organization, user, token, claim, cancellationToken);

        return new InstanceStarted(
            organization.Id,
            organization.Name,
            new SignedIn(
                user.Id,
                user.Name,
                user.Email,
                user.IsAdministrator,
                value.Reveal(),
                expiresAt));
    }

    private static string Validated(string email, string name, string password)
    {
        if (!EmailAddress.IsValid(email))
        {
            throw Refusal.Validation("email", "That is not an email address.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > User.NameLimit)
        {
            throw Refusal.Validation(
                "name", $"A name is between 1 and {User.NameLimit} characters.");
        }

        if (!Password.IsValid(password))
        {
            throw Refusal.Validation(
                "password",
                $"A password is between {Password.MinimumLength} and "
                + $"{Password.MaximumLength} characters.");
        }

        return EmailAddress.Normalize(email);
    }
}
