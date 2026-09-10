using Microsoft.EntityFrameworkCore;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Infrastructure.Persistence;

/// <summary>
/// The identity rows, over the one context (<c>docs/storage.md</c>).
/// </summary>
/// <remarks>
/// Eight of these reach past the organization filter with
/// <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/>, and
/// every one of them is a question asked before anybody is inside an
/// organization: whether this instance has been started, who owns an address, who
/// a token belongs to, the three steps of a device login, and the two an invited
/// person takes before they are anybody here. Specification §9
/// wants the filter central and unavoidable, which is exactly why stepping past
/// it is written out here — in one file, on the queries that have to, with the
/// reason on each.
/// </remarks>
public sealed class IdentityStore(VaultaffeDbContext context) : IIdentityStore
{
    /// <summary>
    /// The instance's only organization (Specification §5). Past the filter,
    /// because whoever is asking has nothing to be inside of yet.
    /// </summary>
    public Task<Organization?> FindTheOrganizationAsync(CancellationToken cancellationToken) =>
        context.Organizations
            .IgnoreQueryFilters()
            .OrderBy(organization => organization.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The caller's own organization. Inside the filter, which for this table is
    /// its own key — a caller reading the name of the organization they are in.
    /// </summary>
    public Task<Organization?> FindOrganizationAsync(CancellationToken cancellationToken) =>
        context.Organizations.FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// A user by the address they sign in with. Past the filter: a sign-in has
    /// only an address to go on, and the organization is what it produces.
    /// </summary>
    public Task<User?> FindUserByEmailAsync(
        string normalizedEmail, CancellationToken cancellationToken) =>
        context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);

    /// <summary>
    /// The token with that hash, and its user. Past the filter: this is the query
    /// that decides which organization the caller is in, so it cannot be asked
    /// from inside one.
    /// </summary>
    public async Task<(Token Token, User User)?> FindTokenByHashAsync(
        byte[] valueHash, CancellationToken cancellationToken)
    {
        var found = await context.Tokens
            .IgnoreQueryFilters()
            .Include(token => token.Bindings)
            .Where(token => token.ValueHash == valueHash)
            .Join(
                context.Users.IgnoreQueryFilters(),
                token => token.UserId,
                user => user.Id,
                (token, user) => new { token, user })
            .FirstOrDefaultAsync(cancellationToken);

        return found is null ? null : (found.token, found.user);
    }

    public async Task StartTheInstanceAsync(
        Organization organization,
        User user,
        Token token,
        InstanceClaim claim,
        CancellationToken cancellationToken)
    {
        context.AddRange(organization, user, token);

        // The claim secret goes in the same transaction that consumes it. An
        // instance that has an administrator and still holds a working claim
        // secret would be one credential nobody knows about, sitting in a row
        // nothing reads (ADR 0019).
        context.Remove(claim);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The claim secret. Past the filter, and the one query here that is not
    /// merely asked before the caller is inside an organization but before there
    /// is one at all.
    /// </summary>
    public Task<InstanceClaim?> FindTheClaimAsync(CancellationToken cancellationToken) =>
        context.InstanceClaims
            .IgnoreQueryFilters()
            .OrderBy(claim => claim.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddClaimAsync(InstanceClaim claim, CancellationToken cancellationToken)
    {
        context.Add(claim);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddTokenAsync(Token token, CancellationToken cancellationToken)
    {
        context.Add(token);

        // One SaveChanges, so whatever else an act changed on a row this store
        // handed out goes in the same transaction — which is what makes redeeming
        // a device login and issuing its token one step rather than two.
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The people of this organization, inside the filter like every ordinary
    /// query: whoever is asking is one of them.
    /// </summary>
    public async Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken) =>
        await context.Users
            .OrderBy(user => user.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<User?> FindUserAsync(Guid id, CancellationToken cancellationToken) =>
        context.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    /// <summary>
    /// The sessions that person is signed in with and that still work. Service
    /// and agent tokens are deliberately not in it: a password reset ends what
    /// the password opened.
    /// </summary>
    public async Task<IReadOnlyList<Token>> ListSessionsOfAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.Tokens
            .Where(token =>
                token.UserId == userId
                && token.Kind == TokenKind.Session
                && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Invitation>> ListInvitationsAsync(
        CancellationToken cancellationToken) =>
        await context.Invitations
            .OrderByDescending(invitation => invitation.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Invitation?> FindInvitationAsync(Guid id, CancellationToken cancellationToken) =>
        context.Invitations.FirstOrDefaultAsync(
            invitation => invitation.Id == id, cancellationToken);

    public async Task AddInvitationAsync(
        Invitation invitation, CancellationToken cancellationToken)
    {
        context.Add(invitation);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// An invitation by the hash of the code in its link. Past the filter:
    /// whoever holds that link is not in an organization yet, and this row is
    /// what puts them in one.
    /// </summary>
    public Task<Invitation?> FindInvitationByCodeHashAsync(
        byte[] codeHash, CancellationToken cancellationToken) =>
        context.Invitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                invitation => invitation.CodeHash == codeHash, cancellationToken);

    /// <summary>
    /// The person an invitation just created, their session, and the spent
    /// invitation, in one transaction. Past the filter, for the same reason: the
    /// request that accepts an invitation carries nothing that authenticated.
    /// </summary>
    public async Task AcceptInvitationAsync(
        User user, Token token, CancellationToken cancellationToken)
    {
        context.AddRange(user, token);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Token>> ListTokensAsync(CancellationToken cancellationToken) =>
        await context.Tokens
            .Include(token => token.Bindings)
            .OrderByDescending(token => token.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Token?> FindTokenAsync(Guid id, CancellationToken cancellationToken) =>
        context.Tokens
            .Include(token => token.Bindings)
            .FirstOrDefaultAsync(token => token.Id == id, cancellationToken);

    public void Add(TokenBinding binding) => context.Add(binding);

    public void Add(EnrollmentBinding binding) => context.Add(binding);

    public void Remove(TokenBinding binding) => context.Remove(binding);

    /// <summary>
    /// Remove one. The bindings go with it through
    /// <c>fk_token_binding_token</c>'s cascade, and nothing else points here: the
    /// change log names its identities rather than keying them, precisely so that
    /// a row can go without taking history with it.
    /// </summary>
    public async Task RemoveTokenAsync(Token token, CancellationToken cancellationToken)
    {
        context.Remove(token);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Begin a login. Past the filter, because the request that starts one
    /// carries nothing that could have been authenticated.
    /// </summary>
    public async Task AddDeviceAuthorizationAsync(
        DeviceAuthorization authorization, CancellationToken cancellationToken)
    {
        context.Add(authorization);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A login by its short code. Past the filter: the browser confirming it
    /// signed in on the page itself and is inside nothing for that request.
    /// </summary>
    public Task<DeviceAuthorization?> FindDeviceAuthorizationByUserCodeAsync(
        string userCode, CancellationToken cancellationToken) =>
        context.DeviceAuthorizations
            .IgnoreQueryFilters()
            .Include(authorization => authorization.ApprovedBindings)
            .FirstOrDefaultAsync(
                authorization => authorization.UserCode == userCode, cancellationToken);

    /// <summary>
    /// A login by the hash of its device code, and whoever confirmed it. Past the
    /// filter: the poll carries no token, which is the whole point of the flow.
    /// </summary>
    public async Task<(DeviceAuthorization Authorization, User? ApprovedBy)?>
        FindDeviceAuthorizationByCodeHashAsync(
            byte[] deviceCodeHash, CancellationToken cancellationToken)
    {
        var authorization = await context.DeviceAuthorizations
            .IgnoreQueryFilters()
            .Include(candidate => candidate.ApprovedBindings)
            .FirstOrDefaultAsync(
                candidate => candidate.DeviceCodeHash == deviceCodeHash, cancellationToken);

        if (authorization is null)
        {
            return null;
        }

        var approvedBy = authorization.ApprovedByUserId is { } userId
            ? await context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken)
            : null;

        return (authorization, approvedBy);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
