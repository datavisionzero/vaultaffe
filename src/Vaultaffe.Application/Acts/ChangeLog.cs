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
/// <para>
/// <b>One log and not two.</b> What is done to people and to tokens goes in here
/// beside what is done to the vault, with no place and an <c>about</c> instead
/// (ADR 0020). The question a person asks is "what happened in this instance and
/// who did it", and they should not have to ask it twice.
/// </para>
/// </remarks>
public sealed class ChangeLog(
    IChangeLogStore entries, ICallerIdentity caller, TimeProvider clock)
{
    public void Record(
        ChangeAction action,
        string? project = null,
        string? environment = null,
        string? secret = null,
        string? about = null) =>
        RecordBy(caller.Required, action, project, environment, secret, about);

    /// <summary>
    /// The same entry, for the two acts that have no caller to read: the first
    /// run and an accepted invitation.
    /// </summary>
    /// <remarks>
    /// Both make the identity they are recorded under, in the same transaction —
    /// so the alternative to this overload is a first person whose arrival is
    /// the one thing the log does not know about. That would break the rule
    /// <see cref="ChangeLogEntry.AboutName"/> relies on: a change of address
    /// records the new one because the old one is in the entry before it, and
    /// there has to <b>be</b> an entry before it.
    /// </remarks>
    public void RecordBy(
        Caller acting,
        ChangeAction action,
        string? project = null,
        string? environment = null,
        string? secret = null,
        string? about = null)
    {
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
            secret,
            about));
    }
}
