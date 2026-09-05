using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Application.Acts;

/// <summary>A session, as the one answer that carries its token.</summary>
public sealed record SignedIn(
    Guid UserId,
    string Name,
    string Email,
    bool IsAdministrator,
    string SessionToken,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Email and password in, a session token out (Specification §6.1). The token is
/// shown exactly once, here.
/// </summary>
/// <remarks>
/// Nothing distinguishes a wrong address from a wrong password in the answer, and
/// the hash is verified even when there is no user, so that the two take the same
/// time. Whether an address belongs to a user of this instance is not something a
/// sign-in form should be able to answer.
/// </remarks>
public sealed class SignIn(
    IIdentityStore identities,
    IPasswordHasher passwords,
    TimeProvider clock)
{
    public async Task<SignedIn> ExecuteAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        var user = await identities.FindUserByEmailAsync(
            EmailAddress.Normalize(email), cancellationToken);

        var correct = await passwords.VerifyAsync(
            user?.PasswordHash ?? passwords.DecoyHash, password ?? string.Empty, cancellationToken);

        if (user is null || !correct)
        {
            throw Refusal.Unauthenticated("That email address and password do not match.");
        }

        var now = clock.GetUtcNow();
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

        await identities.AddTokenAsync(token, cancellationToken);

        return new SignedIn(
            user.Id, user.Name, user.Email, user.IsAdministrator, value.Reveal(), expiresAt);
    }
}
