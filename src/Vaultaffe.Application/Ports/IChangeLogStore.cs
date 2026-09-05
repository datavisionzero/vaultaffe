using Vaultaffe.Domain.History;

namespace Vaultaffe.Application.Ports;

/// <summary>
/// Where a recorded mutation goes.
/// </summary>
/// <remarks>
/// <b>It enlists rather than writes.</b> An entry is committed by the same
/// <c>SaveAsync</c> that commits the change it describes, because a change log
/// written in a transaction of its own is a change log that can disagree with
/// what happened — an entry for a write that was rolled back, or a write with no
/// entry because the second transaction failed. Specification §6.5 asks for a
/// log that says what was done; one that says what was attempted is a different
/// product.
/// </remarks>
public interface IChangeLogStore
{
    /// <summary>Enlist an entry. It reaches the database with the next save.</summary>
    void Add(ChangeLogEntry entry);

    /// <summary>
    /// A page of the log, newest first, narrowed to whatever of
    /// <paramref name="filter"/> is set.
    /// </summary>
    /// <remarks>
    /// The order is by moment and then by id, because one act writes several
    /// entries at the same instant — creating a project writes four — and a page
    /// boundary in the middle of them has to fall in the same place twice.
    /// </remarks>
    Task<IReadOnlyList<ChangeLogEntry>> ReadAsync(
        ChangeLogFilter filter, int limit, int offset, CancellationToken cancellationToken);

    /// <summary>How many entries that filter matches, for a reader that wants to page.</summary>
    Task<int> CountAsync(ChangeLogFilter filter, CancellationToken cancellationToken);
}

/// <summary>
/// What a reader is asking about — a project, an environment of it, a single
/// secret. Null everywhere is the whole organization.
/// </summary>
/// <remarks>
/// By name, because that is what the entries carry: the log outlives the project,
/// environment and secret it talks about, so there is no id in it to filter by
/// (<c>docs/storage.md</c>).
/// </remarks>
public sealed record ChangeLogFilter(
    string? ProjectName = null, string? EnvironmentName = null, string? SecretName = null);
