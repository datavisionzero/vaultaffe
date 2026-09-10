namespace Vaultaffe.Domain.Identities;

/// <summary>
/// What one <see cref="DeviceAuthorization"/> hands over when a person confirms
/// it.
/// </summary>
/// <remarks>
/// There is one table and one flow for both, because the mechanism is the same
/// down to the last rule: two codes, one of them a credential and one of them
/// readable; ten minutes; a person who confirms on some other machine; and a
/// collection that works exactly once. Only the far end differs — a session for
/// the person who confirmed, or a token of the asking machine's own. A second
/// table would have been that whole protocol written twice, and either copy
/// could drift from the other
/// (<see href="../../../docs/adr/0021-an-agent-asks-for-its-own-token.md">ADR 0021</see>).
/// </remarks>
public enum Handover
{
    /// <summary>
    /// A session for the person who confirmed it: `vaultaffe login`
    /// (Specification §6.2).
    /// </summary>
    Session = 1,

    /// <summary>
    /// An agent token of the asking machine's own: `vaultaffe enroll`. The
    /// person who confirms chooses what it may do and how far it reaches, and is
    /// the person it is accountable to afterwards (§6.4).
    /// </summary>
    AgentToken = 2,
}
