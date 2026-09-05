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
}
