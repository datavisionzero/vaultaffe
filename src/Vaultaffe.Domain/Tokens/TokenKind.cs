namespace Vaultaffe.Domain.Tokens;

/// <summary>
/// The three kinds of Specification §5. All three exist from day one even though
/// at first only attribution tells an agent from a person: you do not add a token
/// kind later without a migration, and the change log's identity type has to have
/// a source from the first write path (§6.5).
/// </summary>
public enum TokenKind
{
    /// <summary>What a human receives from <c>vaultaffe login</c>. Lives in the OS keychain.</summary>
    Session = 1,

    /// <summary>For CI and deployments. Defaults to one project, one environment, names and read.</summary>
    Service = 2,

    /// <summary>
    /// An agent's own credential (§6.4). Its point is attribution, not
    /// restriction: the default is the whole organization with every scope, and
    /// the human narrows it at creation.
    /// </summary>
    Agent = 3,
}
