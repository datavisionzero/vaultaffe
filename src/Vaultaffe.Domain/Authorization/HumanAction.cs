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
/// It is an enum rather than four booleans on a permission object because it is
/// closed: a fifth human-only action is a change to the specification, and the
/// compiler should say so at every place that switches on this.
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
        _ => throw new ArgumentOutOfRangeException(
            nameof(action), action, "A human-only action without a refusal."),
    };
}
