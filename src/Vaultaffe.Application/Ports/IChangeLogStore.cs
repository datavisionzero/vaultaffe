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
}
