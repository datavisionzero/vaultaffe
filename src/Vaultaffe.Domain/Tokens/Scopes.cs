namespace Vaultaffe.Domain.Tokens;

/// <summary>
/// What a token may do — a set rather than a read/read-write switch, so that
/// "may write but never read" is expressible (Specification §5).
/// </summary>
[Flags]
public enum Scopes
{
    None = 0,

    /// <summary>Read secret names and their status, never a value. The normal case for an agent.</summary>
    Names = 1,

    /// <summary>Read values. In a secrets manager this is the dangerous one, not the harmless one.</summary>
    Read = 2,

    /// <summary>Create secrets, set and roll back values, create projects and environments.</summary>
    Write = 4,

    /// <summary>Delete recoverably, and restore. Purging is not a scope — it is human-only (§6.4).</summary>
    Delete = 8,

    /// <summary>What an agent token carries unless the human narrows it (§6.4).</summary>
    Everything = Names | Read | Write | Delete,

    /// <summary>What a service token carries unless asked otherwise (§6.4).</summary>
    ServiceDefault = Names | Read,
}

/// <summary>
/// What "carries this scope" means, in one place. A set contains another when
/// every flag of the wanted one is in it — asking for <c>read</c> and
/// <c>write</c> is one question and not two.
/// </summary>
public static class ScopeSets
{
    public static bool Includes(this Scopes granted, Scopes wanted) => (granted & wanted) == wanted;
}
