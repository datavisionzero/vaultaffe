using Vaultaffe.Domain.History;

namespace Vaultaffe.Application.Ports;

/// <summary>
/// The access summary: first and last use per identity and secret
/// (Specification §6.5).
/// </summary>
/// <remarks>
/// A port of its own rather than two more methods on <see cref="ISecretStore"/>,
/// because it is the one thing in this product written on a <b>read</b>. That
/// difference is worth a name: everything else here records what changed, and a
/// method for this sitting among the ones that seal and delete values would
/// invite somebody to record a read on a write path, which is exactly the
/// execution history §6.5 declines to promise.
/// </remarks>
public interface ISecretAccessStore
{
    /// <summary>
    /// Say that this identity has just read that secret: the first moment if
    /// there is none yet, and the last moment either way.
    /// </summary>
    /// <remarks>
    /// One statement, and it commits on its own. Two processes reading the same
    /// key at the same moment must not race each other into a duplicate row, and
    /// a summary that failed must not take a value read down with it — the read
    /// happened, and the honest record of it is best effort by design.
    /// </remarks>
    Task RecordAsync(
        Guid secretId,
        (Guid Id, IdentityType Type, string Name) identity,
        DateTimeOffset moment,
        CancellationToken cancellationToken);

    /// <summary>Every identity that has read that secret, the most recent first.</summary>
    Task<IReadOnlyList<SecretAccess>> SummaryAsync(
        Guid secretId, CancellationToken cancellationToken);
}
