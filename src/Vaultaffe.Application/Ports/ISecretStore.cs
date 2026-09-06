using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.Application.Ports;

/// <summary>
/// The secret rows, and the values they used to hold.
/// </summary>
/// <remarks>
/// Everything here comes back sealed. <b>This port never sees a value in the
/// clear</b> — opening one is <see cref="IKeyRing"/>'s, and the act that wanted
/// it asks that. The separation is the point: the thing that talks to the
/// database and the thing that holds key material are two answers, and neither
/// is in a position to log the other's.
/// <para>
/// Like <see cref="IProjectStore"/>, nothing here reaches past the organization
/// filter, and a deleted row is still a row.
/// </para>
/// </remarks>
public interface ISecretStore
{
    /// <summary>Every secret of that environment, deleted ones left out, by name.</summary>
    Task<IReadOnlyList<Secret>> ListAsync(Guid environmentId, CancellationToken cancellationToken);

    /// <summary>
    /// The secrets of several environments at once, deleted ones left out — what
    /// the missing-key notice compares, in one query rather than one per
    /// environment (Specification §6.1).
    /// </summary>
    Task<IReadOnlyList<Secret>> ListAsync(
        IReadOnlyList<Guid> environmentIds, CancellationToken cancellationToken);

    /// <summary>That secret of that environment, deleted or not, or null.</summary>
    Task<Secret?> FindAsync(Guid environmentId, string name, CancellationToken cancellationToken);

    /// <summary>The deleted secrets of that environment, newest deletion first.</summary>
    Task<IReadOnlyList<Secret>> ListDeletedAsync(
        Guid environmentId, CancellationToken cancellationToken);

    void Add(Secret secret);

    /// <summary>
    /// Mark a deleted secret for permanent removal, its retained values with it
    /// — the one cascade in this schema, and the case it was declared for
    /// (<c>docs/storage.md</c>).
    /// </summary>
    void Purge(Secret secret);

    /// <summary>
    /// Mark everything this secret used to hold for permanent removal, keeping
    /// the secret and its current value. The headline case: after a suspected
    /// compromise, a key's history genuinely disappears (§6.5).
    /// </summary>
    Task PurgeHistoryAsync(Guid secretId, CancellationToken cancellationToken);

    /// <summary>
    /// Keep a value the secret no longer holds, and drop whatever that put over
    /// the bounds of <see cref="ValueHistory"/>. One call, because a version
    /// added without the trim that goes with it is how a bounded history stops
    /// being one.
    /// </summary>
    void Keep(SecretValueVersion version, IReadOnlyList<SecretValueVersion> falling);

    /// <summary>
    /// What this secret used to hold, newest first — for the trim, and for the
    /// rollback and the history that read the same rows.
    /// </summary>
    Task<IReadOnlyList<SecretValueVersion>> HistoryAsync(
        Guid secretId, CancellationToken cancellationToken);

    /// <summary>
    /// The keys the notice was told not to mention in that environment again.
    /// </summary>
    Task<IReadOnlyList<DismissedKey>> DismissalsAsync(
        Guid environmentId, CancellationToken cancellationToken);

    void Add(DismissedKey dismissal);

    /// <summary>
    /// Take a dismissal back, so that the notice mentions that key here again. A
    /// plain removal: a dismissal is the one thing in this schema with nothing to
    /// recover it from, because it is recovered by making it again.
    /// </summary>
    void Remove(DismissedKey dismissal);

    Task SaveAsync(CancellationToken cancellationToken);
}
