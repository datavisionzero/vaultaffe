using Vaultaffe.Domain.Authorization;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Domain.Refusals;

/// <summary>
/// An act saying no, with the code that names why
/// (<see cref="RefusalCode"/>) and whatever a caller needs beside it.
/// </summary>
/// <remarks>
/// An exception rather than a return type, because a refusal is not one of the
/// answers a caller chooses between: every act that can refuse would otherwise
/// return a union its callers have to unwrap on the way up through three layers,
/// and the one place that turns it into an HTTP response is the same place
/// either way. What that place is, is the API's exception handler.
/// <para>
/// <b>A refusal never carries a secret value.</b> Neither does a log line, and
/// for the same reason: Specification §6.5 makes a value in either a bug rather
/// than an untidiness. <see cref="Detail"/> says what was refused, not what it
/// was refused about, and it is written for a person to read.
/// </para>
/// </remarks>
public sealed class Refusal : Exception
{
    public Refusal(
        RefusalCode code,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        : base(detail ?? code.ToString())
    {
        Code = code;
        Detail = detail;
        Extensions = extensions;
    }

    public RefusalCode Code { get; }

    /// <summary>One sentence for a person, or null when the code says it all.</summary>
    public string? Detail { get; }

    /// <summary>What a client needs beside the code — the members of the problem document.</summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; }

    /// <summary>One field, and what is wrong with it.</summary>
    public static Refusal Validation(string field, string message) =>
        new(
            RefusalCode.Validation,
            message,
            new Dictionary<string, object?> { ["errors"] = new Dictionary<string, string[]> { [field] = [message] } });

    public static Refusal NotFound(string detail) => new(RefusalCode.NotFound, detail);

    public static Refusal Unauthenticated(string detail) => new(RefusalCode.Unauthenticated, detail);

    public static Refusal Forbidden(string detail) => new(RefusalCode.Forbidden, detail);

    /// <summary>
    /// One of the short list only a person may do (Specification §6.4). It names
    /// the action and never a command: which command that is depends on which
    /// client is asking, and suggesting one that does not exist is worse than
    /// suggesting none (ADR 0010).
    /// </summary>
    public static Refusal HumanOnly(HumanAction action) =>
        new(
            RefusalCode.HumanOnly,
            HumanActions.RefusalOf(action),
            new Dictionary<string, object?> { ["humanAction"] = HumanActions.NameOf(action) });

    /// <summary>
    /// The token is missing a scope. It carries both sets, because "you may not"
    /// without "you would have needed this" leaves a caller guessing at a token
    /// it cannot see.
    /// </summary>
    public static Refusal InsufficientScope(Scopes required, Scopes granted) =>
        new(
            RefusalCode.InsufficientScope,
            "This token does not carry "
            + string.Join(" and ", ScopeNames.NamesOf(required & ~granted))
            + ". A human widens a token by issuing a new one; scopes are set when it is created.",
            new Dictionary<string, object?>
            {
                ["requiredScopes"] = ScopeNames.NamesOf(required),
                ["grantedScopes"] = ScopeNames.NamesOf(granted),
            });

    /// <summary>
    /// The token is bound to particular projects and environments, and this is
    /// not one of them (§6.4). The ids are in it because they are not secret and
    /// a caller comparing them against its own binding is how it finds out what
    /// it was pointed at.
    /// </summary>
    public static Refusal OutOfReach(Guid? projectId = null, Guid? environmentId = null) =>
        new(
            RefusalCode.OutOfReach,
            projectId is null
                ? "This token is bound to particular projects, and this needs one that reaches "
                    + "the whole organization. A human issues a token that does."
                : "This token is bound to particular projects and environments, and that is not "
                    + "one of them. A human issues a token that reaches there.",
            new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["environmentId"] = environmentId,
            });

    /// <summary>
    /// The name is in use. Deliberately its own code and not a validation
    /// failure: nothing about the name is malformed, and the surprising case —
    /// a deleted object keeping its name reserved for as long as it can be
    /// restored (Specification §6.5) — is one a client should be able to
    /// recognise rather than read.
    /// </summary>
    public static Refusal NameTaken(string detail, bool bySomethingDeleted) =>
        new(
            RefusalCode.NameTaken,
            detail,
            new Dictionary<string, object?> { ["takenBySomethingDeleted"] = bySomethingDeleted });

    /// <summary>Deleted longer ago than the recovery window. There is nothing to restore.</summary>
    public static Refusal NotRecoverable(string detail) => new(RefusalCode.NotRecoverable, detail);

    /// <summary>
    /// The key already holds a value, and overwriting one is explicit
    /// (Specification §6.2). Its own code because the remedy is one word and a
    /// client can offer it: say so and try again.
    /// </summary>
    public static Refusal ReplaceRequired(string name) =>
        new(
            RefusalCode.ReplaceRequired,
            $"'{name}' already holds a value. Overwriting one is as destructive as deleting it, "
            + "so it has to be asked for.",
            new Dictionary<string, object?> { ["secretName"] = name });
}
