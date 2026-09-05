using Microsoft.EntityFrameworkCore;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;

namespace Vaultaffe.Infrastructure.Persistence;

/// <summary>
/// Where a recorded mutation goes (<c>docs/storage.md</c>).
/// </summary>
/// <remarks>
/// It enlists and does not write. The entry reaches the database with the same
/// <c>SaveChanges</c> that commits the change it describes, because this context
/// is the one the acting store also holds — so an act that fails after recording
/// writes neither, and there is no window in which the log and the data disagree.
/// </remarks>
public sealed class ChangeLogStore(VaultaffeDbContext context) : IChangeLogStore
{
    public void Add(ChangeLogEntry entry) => context.Add(entry);

    public async Task<IReadOnlyList<ChangeLogEntry>> ReadAsync(
        ChangeLogFilter filter, int limit, int offset, CancellationToken cancellationToken) =>
        await Matching(filter)
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(ChangeLogFilter filter, CancellationToken cancellationToken) =>
        Matching(filter).CountAsync(cancellationToken);

    private IQueryable<ChangeLogEntry> Matching(ChangeLogFilter filter)
    {
        var entries = context.ChangeLog.AsQueryable();

        if (filter.ProjectName is { } project)
        {
            entries = entries.Where(entry => entry.ProjectName == project);
        }

        if (filter.EnvironmentName is { } environment)
        {
            entries = entries.Where(entry => entry.EnvironmentName == environment);
        }

        if (filter.SecretName is { } secret)
        {
            entries = entries.Where(entry => entry.SecretName == secret);
        }

        return entries;
    }
}
