namespace Vaultaffe.Domain.History;

/// <summary>
/// What was done. The change log records mutations, not reads: <c>run</c> reads
/// values, but a successful read does not prove an application started, and the
/// MVP does not pretend to an execution history (Specification §6.5).
/// </summary>
/// <remarks>
/// The first eight are things done to the vault and carry a place — a project,
/// an environment, a secret. The rest are things done to people and to
/// credentials: they carry no place, and what they were about is in
/// <see cref="ChangeLogEntry.AboutName"/>
/// (<see href="../../../docs/adr/0020-one-change-log-and-not-two.md">ADR 0020</see>).
/// A sign-in is in neither list, because it changes nothing.
/// </remarks>
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

    // What is done to people and to credentials rather than to the vault. These
    // carry no project, environment or secret and name their subject in
    // `AboutName` instead — the one question a change log has to be able to
    // answer about an instance is "who changed the doors", and until these
    // existed it could not (ADR 0020).

    /// <summary>An invitation was written out. Its subject is the address invited.</summary>
    Invited = 9,

    /// <summary>An invitation nobody had used was withdrawn.</summary>
    InvitationWithdrawn = 10,

    /// <summary>
    /// Somebody became a person of this organization: the first run, or an
    /// invitation accepted. The beginning of every chain below — without it an
    /// address change would appear to come from nowhere.
    /// </summary>
    Joined = 11,

    /// <summary>
    /// A password was set: an administrator setting somebody else's, or a person
    /// changing their own. Never the password, and there is nowhere to put one.
    /// </summary>
    PasswordSet = 12,

    /// <summary>
    /// The address somebody signs in with changed. The sharpest entry in this
    /// log: afterwards a different person may sign in under the same name.
    /// </summary>
    EmailChanged = 13,

    /// <summary>Somebody was taken out of the organization.</summary>
    Deactivated = 14,

    /// <summary>And put back.</summary>
    Reactivated = 15,

    /// <summary>A person's own name changed — what this log calls them.</summary>
    PersonRenamed = 16,

    /// <summary>The organization was renamed.</summary>
    OrganizationRenamed = 17,

    /// <summary>
    /// A token was issued. A key to the vault, handed to a machine; §6.4 makes
    /// it human-only for that reason, and a log that did not say so was missing
    /// the entry it most needed.
    /// </summary>
    TokenCreated = 18,

    /// <summary>A token was revoked and works nowhere from now on.</summary>
    TokenRevoked = 19,

    /// <summary>
    /// A token was changed: what it is called, what it may do, or how far it
    /// reaches. Widening one hands a wider credential to whoever already holds
    /// that value without issuing anything, so this is the entry that says a key
    /// already in the world opens more doors than it did.
    /// </summary>
    TokenChanged = 20,

    /// <summary>
    /// A revoked token's row was removed for good. The counterpart of
    /// <see cref="Purged"/> for a credential rather than for the vault, and
    /// human-only for the same reason: it is the one removal this product cannot
    /// undo. The entries the token signed keep its name, which is what
    /// <c>identity_name</c> has always been for.
    /// </summary>
    TokenPurged = 21,
}
