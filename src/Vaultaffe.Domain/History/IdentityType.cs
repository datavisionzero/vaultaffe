namespace Vaultaffe.Domain.History;

/// <summary>
/// What kind of thing acted. Recorded beside the identity itself, because with
/// writing agents that is the interesting question (Specification §6.5) — and the
/// token kind is what tells us.
/// </summary>
public enum IdentityType
{
    /// <summary>A person, at a browser session or under their own session token.</summary>
    HumanSession = 1,

    /// <summary>CI, a deployment, something with no human waiting on it.</summary>
    ServiceToken = 2,

    /// <summary>
    /// An agent, under its own token. This is the entry that stops the log from
    /// saying a person's name for everything an agent did in their terminal.
    /// </summary>
    AgentToken = 3,
}
