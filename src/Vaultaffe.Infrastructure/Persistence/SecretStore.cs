using Microsoft.EntityFrameworkCore;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.Infrastructure.Persistence;

/// <summary>
/// The secret rows and the values they used to hold (<c>docs/storage.md</c>).
/// </summary>
/// <remarks>
/// Everything here is sealed on the way in and sealed on the way out. This file
/// holds no key material and calls nothing that does — opening a value is the key
/// ring's, one layer up, which is what keeps the thing that talks to Postgres and
/// the thing that holds the master key two different answers.
/// <para>
/// The writes enlist rather than commit, so that a whole imported file, its
/// change-log entries and the versions it superseded reach the database in one
/// transaction. A file half applied is worse than a file refused: nobody can tell
/// by looking which half it was.
/// </para>
/// </remarks>
public sealed class SecretStore(VaultaffeDbContext context) : ISecretStore
{
    public async Task<IReadOnlyList<Secret>> ListAsync(
        Guid environmentId, CancellationToken cancellationToken) =>
        await context.Secrets
            .Where(secret => secret.EnvironmentId == environmentId && secret.DeletedAt == null)
            .OrderBy(secret => secret.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Secret>> ListAsync(
        IReadOnlyList<Guid> environmentIds, CancellationToken cancellationToken) =>
        await context.Secrets
            .Where(secret =>
                environmentIds.Contains(secret.EnvironmentId) && secret.DeletedAt == null)
            .OrderBy(secret => secret.Name)
            .ToListAsync(cancellationToken);

    public Task<Secret?> FindAsync(
        Guid environmentId, string name, CancellationToken cancellationToken) =>
        context.Secrets.FirstOrDefaultAsync(
            secret => secret.EnvironmentId == environmentId && secret.Name == name,
            cancellationToken);

    public async Task<IReadOnlyList<Secret>> ListDeletedAsync(
        Guid environmentId, CancellationToken cancellationToken) =>
        await context.Secrets
            .Where(secret => secret.EnvironmentId == environmentId && secret.DeletedAt != null)
            .OrderByDescending(secret => secret.DeletedAt)
            .ToListAsync(cancellationToken);

    public void Add(Secret secret) => context.Add(secret);

    /// <summary>The retained values follow, by the cascade the schema declares.</summary>
    public void Purge(Secret secret) => context.Remove(secret);

    /// <summary>
    /// Tracked removal rather than ExecuteDelete, so that the change-log entry
    /// recorded about this purge commits in the same transaction as the rows it
    /// describes.
    /// </summary>
    public async Task PurgeHistoryAsync(Guid secretId, CancellationToken cancellationToken) =>
        context.RemoveRange(
            await context.SecretValueVersions
                .Where(version => version.SecretId == secretId)
                .ToListAsync(cancellationToken));

    public void Keep(SecretValueVersion version, IReadOnlyList<SecretValueVersion> falling)
    {
        context.Add(version);

        // Deleted rather than tombstoned (Specification §6.5): a retained old
        // value is usually a still-valid credential, and a tombstone would be a
        // row that still holds one.
        context.RemoveRange(falling);
    }

    public async Task<IReadOnlyList<SecretValueVersion>> HistoryAsync(
        Guid secretId, CancellationToken cancellationToken) =>
        await context.SecretValueVersions
            .Where(version => version.SecretId == secretId)
            .OrderByDescending(version => version.ReplacedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DismissedKey>> DismissalsAsync(
        Guid environmentId, CancellationToken cancellationToken) =>
        await context.DismissedKeys
            .Where(dismissal => dismissal.EnvironmentId == environmentId)
            .OrderBy(dismissal => dismissal.Name)
            .ToListAsync(cancellationToken);

    public void Add(DismissedKey dismissal) => context.Add(dismissal);

    public void Remove(DismissedKey dismissal) => context.Remove(dismissal);

    public Task SaveAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
