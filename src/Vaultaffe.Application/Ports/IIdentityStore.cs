using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Application.Ports;

/// <summary>
/// The rows identity is kept in: the organization, its users, their tokens, and
/// the logins in progress.
/// </summary>
/// <remarks>
/// One port rather than four, because these rows are only ever read and written
/// together — a first run creates an organization, a user and a token in one act,
/// and a device login ends by creating a token against a user it just looked up.
/// Splitting them would buy nothing but four constructor parameters.
/// <para>
/// Some of these reach past the organization filter of Specification §9, and each
/// says so: a first run, a sign-in, a token authentication and every step of a
/// device login all happen before anybody is inside an organization. Everything
/// else here is filtered like every other query.
/// </para>
/// </remarks>
public interface IIdentityStore
{
    /// <summary>
    /// The instance's only organization, or null before the first run
    /// (Specification §5). Reaches past the filter: whoever asks this is not
    /// inside an organization yet.
    /// </summary>
    Task<Organization?> FindTheOrganizationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The organization the caller is in — the same row as above, asked from
    /// inside it, so the filter answers rather than being stepped past.
    /// </summary>
    Task<Organization?> FindOrganizationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// A user by the address they sign in with, already normalized. Reaches past
    /// the filter, for the same reason.
    /// </summary>
    Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>
    /// The token with that value hash and the user it belongs to, or null.
    /// Reaches past the filter: this is what decides which organization the
    /// caller is in, so it cannot be asked from inside one.
    /// </summary>
    Task<(Token Token, User User)?> FindTokenByHashAsync(
        byte[] valueHash, CancellationToken cancellationToken);

    /// <summary>
    /// The organization, its first user and their first token, in one
    /// transaction. There is no way to add one without the others: an instance
    /// with an organization nobody can enter is not an instance anybody asked
    /// for.
    /// </summary>
    Task StartTheInstanceAsync(
        Organization organization,
        User user,
        Token token,
        InstanceClaim claim,
        CancellationToken cancellationToken);

    /// <summary>
    /// This instance's claim secret, or null once the first run has consumed it
    /// (ADR 0019). Reaches past the filter, and past it further than anything
    /// else here: the row belongs to no organization, because when it matters
    /// there is none.
    /// </summary>
    Task<InstanceClaim?> FindTheClaimAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Put one there. Called at startup rather than by a request: an instance
    /// that waited for somebody to ask before it made its claim secret would
    /// have no secret to print in the log the operator is already reading.
    /// </summary>
    Task AddClaimAsync(InstanceClaim claim, CancellationToken cancellationToken);

    /// <summary>
    /// Add a token and write. Whatever else the acts changed on rows this port
    /// handed out goes with it, in the same transaction — which is what makes
    /// redeeming a device login and issuing its token one step rather than two.
    /// </summary>
    Task AddTokenAsync(Token token, CancellationToken cancellationToken);

    /// <summary>The people of this organization, oldest first, deactivated ones included.</summary>
    Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken);

    /// <summary>One of them, by id.</summary>
    Task<User?> FindUserAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The sessions that person is signed in with and that still work — what a
    /// password reset has to end, since a session opened with the old password
    /// would outlive it otherwise. Their service and agent tokens are not in it:
    /// those never depended on the password.
    /// </summary>
    Task<IReadOnlyList<Token>> ListSessionsOfAsync(
        Guid userId, CancellationToken cancellationToken);

    /// <summary>Every invitation of this organization, newest first, in whatever state.</summary>
    Task<IReadOnlyList<Invitation>> ListInvitationsAsync(CancellationToken cancellationToken);

    Task<Invitation?> FindInvitationAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Write one out. An administrator has asked, so this is inside the filter.</summary>
    Task AddInvitationAsync(Invitation invitation, CancellationToken cancellationToken);

    /// <summary>
    /// An invitation by the hash of the code in its link. Reaches past the
    /// filter: whoever is holding that link is not in an organization yet — the
    /// invitation is what puts them in one.
    /// </summary>
    Task<Invitation?> FindInvitationByCodeHashAsync(
        byte[] codeHash, CancellationToken cancellationToken);

    /// <summary>
    /// The person an accepted invitation just created and the session they
    /// signed up into, in one transaction, together with the invitation that is
    /// now spent. Reaches past the filter, for the same reason.
    /// </summary>
    Task AcceptInvitationAsync(User user, Token token, CancellationToken cancellationToken);

    /// <summary>Every token of this organization, newest first, revoked ones included.</summary>
    Task<IReadOnlyList<Token>> ListTokensAsync(CancellationToken cancellationToken);

    Task<Token?> FindTokenAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Begin one. Reaches past the filter, because the request that starts a
    /// login carries nothing that could have been authenticated.
    /// </summary>
    Task AddDeviceAuthorizationAsync(
        DeviceAuthorization authorization, CancellationToken cancellationToken);

    /// <summary>
    /// A login in progress, by the short code a human typed. Reaches past the
    /// filter: the browser confirming it has signed in on the page itself and is
    /// not inside an organization for the length of that request.
    /// </summary>
    Task<DeviceAuthorization?> FindDeviceAuthorizationByUserCodeAsync(
        string userCode, CancellationToken cancellationToken);

    /// <summary>
    /// A login in progress, by the hash of the long code the CLI polls with, and
    /// the user who confirmed it if anybody has. Reaches past the filter: the
    /// poll carries no token, which is the whole point of the flow — and the user
    /// comes with the row so that nothing else has to be able to read one from
    /// outside an organization.
    /// </summary>
    Task<(DeviceAuthorization Authorization, User? ApprovedBy)?>
        FindDeviceAuthorizationByCodeHashAsync(
            byte[] deviceCodeHash, CancellationToken cancellationToken);

    /// <summary>Write back what the acts changed on rows this port handed out.</summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
