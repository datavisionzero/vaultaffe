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

    /// <summary>That secret of that environment, deleted or not, or null.</summary>
    Task<Secret?> FindAsync(Guid environmentId, string name, CancellationToken cancellationToken);

    void Add(Secret secret);

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

    Task SaveAsync(CancellationToken cancellationToken);
}
