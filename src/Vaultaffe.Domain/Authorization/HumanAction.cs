namespace Vaultaffe.Domain.Authorization;

/// <summary>
/// The short list of Specification §6.4: what only a person may do.
/// </summary>
/// <remarks>
/// Two things put an action here and nothing else does — <b>an agent's mistake
/// could not be undone</b>, or <b>the output is itself a secret</b>. Everything
/// else an agent may do by default: read, run, set, delete recoverably, restore,
/// create projects and environments, roll back. The list is deliberately short,
/// because the product's answer to an agent is attribution and not restriction.
/// <para>
/// It is an enum rather than a handful of booleans on a permission object because
/// it is closed: another human-only action is a change to the specification, and
/// the compiler should say so at every place that switches on this. Four of the
/// eight are a token's whole lifecycle after it exists — <see cref="RevokeToken"/>,
/// <see cref="ChangeToken"/>, <see cref="RotateToken"/> and
/// <see cref="PurgeToken"/> — because §6.4's clause about creating and revoking
/// is really about who holds the pen on a credential, and renaming is the only
/// part of that a holder could not abuse.
/// </para>
/// <para>
/// <see cref="RotateToken"/> carries the one exception this list has. Being
/// human-only is a property of the action everywhere else here; there it is a
/// property of the action and the object together, because an agent replacing
/// the value of the token it is already holding gains nothing it did not have
/// and hands the answer to nobody
/// (<see href="../../../docs/adr/0022-an-agent-renews-its-own-token.md">ADR 0022</see>).
/// </para>
/// </remarks>
public enum HumanAction
{
    /// <summary>
    /// Removing value versions or deleted objects for good. The one deletion this
    /// product cannot undo — everything else an agent deletes is recoverable
    /// (§6.5).
    /// </summary>
    Purge = 1,

    /// <summary>
    /// Creating a service or an agent token. The answer carries a value, so a
    /// token an agent created through the CLI would be printed to stdout and thus
    /// into its own context (§6.1).
    /// </summary>
    CreateToken = 2,

    /// <summary>
    /// Revoking one. It is how a human takes a credential back, including the
    /// agent's own — an agent that could revoke tokens could revoke the one
    /// watching it.
    /// </summary>
    RevokeToken = 3,

    /// <summary>
    /// The organization and its users: inviting somebody, removing them, changing
    /// what the organization is. Who may be in here is a decision about people.
    /// </summary>
    AdministerOrganization = 4,

    /// <summary>
    /// Exporting an environment. It writes every value in plaintext, which is the
    /// contradiction <c>inject</c> was rejected for (§7); it stays because a way
    /// back out is part of being trustworthy, and §6.2 puts it behind a session
    /// for exactly the reason the rest of this list exists — the output is itself
    /// a secret, all of them at once.
    /// </summary>
    Export = 5,

    /// <summary>
    /// Changing a token: what it is called, what it may do, how far it reaches. It
    /// is here for the mirror image of the reason revoking is — an agent that could
    /// widen a token could widen its own, and a credential that grants itself
    /// scopes is not one anybody issued.
    /// </summary>
    ChangeToken = 6,

    /// <summary>
    /// Removing a revoked token's row for good. <see cref="Purge"/> pointed at a
    /// credential rather than at the vault, and human-only for the same reason:
    /// it is the one removal this product cannot undo.
    /// </summary>
    PurgeToken = 7,

    /// <summary>
    /// Replacing a token's value: the row it had is revoked and a successor is
    /// issued beside it, carrying the same name, scopes and reach. It is here for
    /// the reason <see cref="CreateToken"/> is — the answer is a value, and one
    /// printed into an agent's terminal is in that agent's context.
    /// <para>
    /// And it is the one action on this list an agent may do to <b>one</b>
    /// object: the token it is itself holding. That is not a hole in the rule but
    /// the rule read carefully — the holder of a value replacing it with another
    /// value of the same reach issues nothing it did not already have, and the
    /// answer goes to a file rather than to a screen (ADR 0022).
    /// </para>
    /// </summary>
    RotateToken = 8,
}

/// <summary>
/// How the human-only actions are named, and what a refusal says about them.
/// </summary>
/// <remarks>
/// <b>No sentence here names a command.</b> The server knows which action was
/// refused; it does not know which release of which client is asking, and
/// Specification §8, scenario 5 is only kept if the command the agent hands to
/// its human is one that exists. So the refusal carries the action as
/// <c>humanAction</c> and the client renders the command it actually has
/// (<c>docs/adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md</c>).
/// </remarks>
public static class HumanActions
{
    /// <summary>The wire spelling: <see cref="HumanAction.CreateToken"/> is <c>create-token</c>.</summary>
    public static string NameOf(HumanAction action) => action switch
    {
        HumanAction.Purge => "purge",
        HumanAction.CreateToken => "create-token",
        HumanAction.RevokeToken => "revoke-token",
        HumanAction.AdministerOrganization => "administer-organization",
        HumanAction.Export => "export",
        HumanAction.ChangeToken => "change-token",
        HumanAction.PurgeToken => "purge-token",
        HumanAction.RotateToken => "rotate-token",
        _ => throw new ArgumentOutOfRangeException(
            nameof(action), action, "A human-only action without a name."),
    };

    /// <summary>
    /// What the refusal says, in one sentence for whoever reads it: what was
    /// asked for, why it is a person's to do, and that the way on is to hand it
    /// to one.
    /// </summary>
    public static string RefusalOf(HumanAction action) => action switch
    {
        HumanAction.Purge =>
            "Purging is reserved for a person: it is the one deletion this product cannot undo. "
            + "Hand it to a human, who does it under their own session.",
        HumanAction.CreateToken =>
            "Creating a token is reserved for a person: the answer is itself a secret, and one "
            + "created here would be printed into this context. Hand it to a human, who creates "
            + "it under their own session and gives you the value.",
        HumanAction.RevokeToken =>
            "Revoking a token is reserved for a person: it is how a human takes a credential "
            + "back. Hand it to a human, who does it under their own session.",
        HumanAction.AdministerOrganization =>
            "Administering the organization and its users is reserved for a person. Hand it to a "
            + "human, who does it under their own session.",
        HumanAction.Export =>
            "Exporting is reserved for a person: it writes every value of an environment in "
            + "plaintext at once. Read the one value you need instead, or hand the export to a "
            + "human, who does it under their own session.",
        HumanAction.ChangeToken =>
            "Changing a token is reserved for a person: what a credential may do and how far it "
            + "reaches is a person's to widen, never its holder's. Hand it to a human, who does "
            + "it under their own session.",
        HumanAction.PurgeToken =>
            "Removing a revoked token for good is reserved for a person: it is the one removal "
            + "this product cannot undo. Hand it to a human, who does it under their own "
            + "session.",
        HumanAction.RotateToken =>
            "Rotating a token is reserved for a person: the answer is itself a secret, and one "
            + "made here would be printed into this context. The exception is an agent replacing "
            + "the token it is holding itself, by the path that writes the value to a file "
            + "instead of showing it — and this is not that. Hand it to a human, who does it "
            + "under their own session.",
        _ => throw new ArgumentOutOfRangeException(
            nameof(action), action, "A human-only action without a refusal."),
    };
}
