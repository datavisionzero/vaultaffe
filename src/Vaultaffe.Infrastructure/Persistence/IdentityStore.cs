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
/// Six of these reach past the organization filter with
/// <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/>, and
/// every one of them is a question asked before anybody is inside an
/// organization: whether this instance has been started, who owns an address, who
/// a token belongs to, and the three steps of a device login. Specification §9
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
        Organization organization, User user, Token token, CancellationToken cancellationToken)
    {
        context.AddRange(organization, user, token);

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

    public async Task<IReadOnlyList<Token>> ListTokensAsync(CancellationToken cancellationToken) =>
        await context.Tokens
            .Include(token => token.Bindings)
            .OrderByDescending(token => token.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Token?> FindTokenAsync(Guid id, CancellationToken cancellationToken) =>
        context.Tokens
            .Include(token => token.Bindings)
            .FirstOrDefaultAsync(token => token.Id == id, cancellationToken);

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
