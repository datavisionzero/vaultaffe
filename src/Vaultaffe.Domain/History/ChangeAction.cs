namespace Vaultaffe.Domain.History;

/// <summary>
/// What was done. The change log records mutations, not reads: <c>run</c> reads
/// values, but a successful read does not prove an application started, and the
/// MVP does not pretend to an execution history (Specification §6.5).
/// </summary>
public enum ChangeAction
{
    Created = 1,
    Renamed = 2,

    /// <summary>A value was written — the first one, or over an existing one.</summary>
    ValueSet = 3,

    /// <summary>A value was rolled back to a retained version. A write like any other.</summary>
    ValueRolledBack = 4,

    /// <summary>An empty placeholder was created for a human to fill.</summary>
    PlaceholderCreated = 5,

    /// <summary>Deleted recoverably.</summary>
    Deleted = 6,

    /// <summary>Restored within its window.</summary>
    Restored = 7,

    /// <summary>
    /// Retained versions or a deleted object were removed before their deadline.
    /// Human-only: it is the one action that destroys the undo button.
    /// </summary>
    Purged = 8,
}
