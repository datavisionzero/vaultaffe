using Microsoft.EntityFrameworkCore;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;

namespace Vaultaffe.Infrastructure.Persistence;

/// <summary>
/// The access summary (<c>docs/storage.md</c>), and the one write in this product
/// that happens on a read.
/// </summary>
/// <remarks>
/// <b>One statement, not a read followed by a write.</b> Two <c>run</c>s starting
/// at the same moment under the same token would otherwise race each other into
/// a duplicate row, and the loser of that race would turn a value read into a
/// 500. <c>on conflict … do update</c> makes it one atomic step, and
/// <c>greatest</c> keeps the rule the type declares: <c>first_at</c> is written
/// once, <c>last_at</c> only moves forward.
/// <para>
/// It is also the one place here that writes SQL rather than tracked entities.
/// The organization is passed rather than inferred, because a statement of this
/// shape steps past the query filter — the secret it names was found through a
/// filtered query one layer up, and the row it writes carries the caller's own
/// organization and nobody else's.
/// </para>
/// <para>
/// And it commits by itself, which is deliberate: a read is not part of a
/// transaction that changes anything, and a summary that could not be written
/// must not take the value read down with it.
/// </para>
/// </remarks>
public sealed class SecretAccessStore(VaultaffeDbContext context, IOrganizationScope scope)
    : ISecretAccessStore
{
    public async Task RecordAsync(
        Guid secretId,
        (Guid Id, IdentityType Type, string Name) identity,
        DateTimeOffset moment,
        CancellationToken cancellationToken)
    {
        if (scope.OrganizationId is not { } organizationId)
        {
            return;
        }

        try
        {
            await context.Database.ExecuteSqlAsync(
                $"""
                insert into secret_access
                    (id, organization_id, secret_id, identity_id, identity_type,
                     identity_name, first_at, last_at)
                values
                    ({Guid.NewGuid()}, {organizationId}, {secretId}, {identity.Id},
                     {(int)identity.Type}, {identity.Name}, {moment}, {moment})
                on conflict (secret_id, identity_id) do update set
                    identity_name = excluded.identity_name,
                    last_at = greatest(secret_access.last_at, excluded.last_at)
                """,
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Best effort, and said so in the port: the read happened, and a
            // summary is not worth refusing a value over.
        }
    }

    public async Task<IReadOnlyList<SecretAccess>> SummaryAsync(
        Guid secretId, CancellationToken cancellationToken) =>
        await context.SecretAccesses
            .Where(access => access.SecretId == secretId)
            .OrderByDescending(access => access.LastAt)
            .ToListAsync(cancellationToken);
}
