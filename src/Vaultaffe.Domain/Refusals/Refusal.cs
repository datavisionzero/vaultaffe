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
}
