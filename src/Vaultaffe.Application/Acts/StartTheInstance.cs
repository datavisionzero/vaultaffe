using Vaultaffe.Application.Ports;
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
/// It happens exactly once and it is unauthenticated, because there is nobody to
/// authenticate as yet. What stops a stranger from being that first user is that
/// the second attempt is refused — an instance that has been started cannot be
/// started again, and an operator who reaches theirs before anyone else does owns
/// it.
/// <para>
/// That is a real exposure between `docker compose up` and the first sign-in, and
/// it is the one every self-hosted product with a first-run wizard has. It is
/// named here rather than pretended away; the operations guide is where "do this
/// now, not tomorrow" belongs.
/// </para>
/// </remarks>
public sealed class StartTheInstance(
    IIdentityStore identities,
    IPasswordHasher passwords,
    TimeProvider clock)
{
    /// <summary>The name the MVP's one organization gets (Specification §6.1).</summary>
    public const string DefaultOrganizationName = "Default";

    public async Task<InstanceStarted> ExecuteAsync(
        string email, string name, string password, CancellationToken cancellationToken)
    {
        if (await identities.FindTheOrganizationAsync(cancellationToken) is not null)
        {
            throw new Refusal(
                RefusalCode.AlreadyStarted,
                "This instance has already been started. Sign in, or ask an administrator "
                + "to add you.");
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

        await identities.StartTheInstanceAsync(organization, user, token, cancellationToken);

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
