using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;

namespace Vaultaffe.Application.Acts;

/// <summary>
/// What every write path in this product says about itself (Specification §6.5).
/// </summary>
/// <remarks>
/// It exists from the first write path rather than as a later addition, and that
/// is a decision the build order makes explicitly: a change log retrofitted onto
/// paths that already work is a change log with holes in it, and the holes are
/// discovered by somebody asking who changed something.
/// <para>
/// <b>Nothing here takes a value, and there is nowhere to put one.</b> The entry
/// records what was done and to which name — the type carries no value column and
/// this signature offers no value parameter, which is the same rule stated twice
/// on purpose.
/// </para>
/// <para>
/// The identity and its type come off the caller, so an agent acting under its
/// own token is recorded as an agent rather than as the person whose terminal it
/// sits in — which is the whole reason agent tokens exist (§6.4).
/// </para>
/// </remarks>
public sealed class ChangeLog(
    IChangeLogStore entries, ICallerIdentity caller, TimeProvider clock)
{
    public void Record(
        ChangeAction action,
        string? project = null,
        string? environment = null,
        string? secret = null)
    {
        var acting = caller.Required;
        var (id, type, name) = acting.Identity;

        entries.Add(new ChangeLogEntry(
            Guid.NewGuid(),
            acting.OrganizationId,
            clock.GetUtcNow(),
            id,
            type,
            name,
            action,
            project,
            environment,
            secret));
    }
}
