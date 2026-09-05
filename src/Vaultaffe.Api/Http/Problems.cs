using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Vaultaffe.Api.Http;

/// <summary>
/// The one place a refusal becomes a problem document (<c>docs/api.md</c>): every
/// error this API returns is <c>application/problem+json</c> with a stable
/// <c>type</c> whose last segment is the code a client switches on, and that same
/// code as a member beside it.
/// </summary>
/// <remarks>
/// The status of a code lives here and nowhere else. HTTP's word for a refusal
/// is the status; the CLI's word for it is the code and an exit code of its own,
/// and neither has to be derivable from the other.
/// <para>
/// <b>A problem document never carries a secret value.</b> Specification §6.5
/// makes a value in a log line or an error message a bug rather than an
/// untidiness, and <c>detail</c> is the field that invites one. It says what was
/// refused, not what was refused about.
/// </para>
/// </remarks>
public static class Problems
{
    /// <summary>What every one of these responses is.</summary>
    public const string ContentType = "application/problem+json";

    /// <summary>
    /// What a <c>type</c> starts with. Relative on purpose: RFC 9457 resolves it
    /// against the request, so the document points at the instance that served
    /// it — which is the one that knows which codes it actually raises.
    /// </summary>
    private const string TypePrefix = "/problems/";

    /// <summary>The wire spelling: <see cref="ProblemCode.ClientTooOld"/> is <c>client-too-old</c>.</summary>
    public static string CodeOf(ProblemCode code) =>
        JsonNamingPolicy.KebabCaseLower.ConvertName(code.ToString());

    /// <summary>The code again, spelled as the <c>type</c> a client may dereference.</summary>
    public static string TypeOf(ProblemCode code) => TypePrefix + CodeOf(code);

    /// <summary>Which code a wire spelling names, or null when this build has no such code.</summary>
    public static ProblemCode? Parse(string? code) =>
        Enum.GetValues<ProblemCode>().Cast<ProblemCode?>()
            .FirstOrDefault(known => CodeOf(known!.Value) == code);

    public static int StatusOf(ProblemCode code) => code switch
    {
        ProblemCode.ClientVersionUnreadable => StatusCodes.Status400BadRequest,
        ProblemCode.NotFound or ProblemCode.UnsupportedApiVersion => StatusCodes.Status404NotFound,
        ProblemCode.ClientTooOld => StatusCodes.Status426UpgradeRequired,
        ProblemCode.Internal => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A problem code without a status."),
    };

    public static string TitleOf(ProblemCode code) => code switch
    {
        ProblemCode.NotFound => "Nothing by that name",
        ProblemCode.UnsupportedApiVersion => "This instance does not serve that version of the API",
        ProblemCode.ClientTooOld => "This client is older than this instance accepts",
        ProblemCode.ClientVersionUnreadable => "The announced client version is not a version",
        ProblemCode.Internal => "Something went wrong on the server",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A problem code without a title."),
    };

    /// <summary>The document for <paramref name="code"/>.</summary>
    public static ProblemDetails Document(
        ProblemCode code,
        string? detail,
        string? instance = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var document = new ProblemDetails
        {
            Type = TypeOf(code),
            Title = TitleOf(code),
            Status = StatusOf(code),
            Detail = detail,
            Instance = instance,
        };

        // The code travels twice: as the last segment of `type`, because that is
        // where RFC 9457 puts identity, and as a member of its own, because a
        // client should not have to take a URI apart to learn which refusal it
        // is looking at.
        document.Extensions["code"] = CodeOf(code);

        foreach (var (key, value) in extensions ?? new Dictionary<string, object?>())
        {
            document.Extensions[key] = value;
        }

        return document;
    }

    /// <summary>The document as an endpoint's result.</summary>
    public static IResult Result(
        ProblemCode code,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null) =>
        Results.Problem(Document(code, detail, instance: null, extensions));

    /// <summary>
    /// Writes the document straight to the response, for the refusals that
    /// happen before an endpoint runs.
    /// </summary>
    public static async Task WriteAsync(
        HttpContext context,
        ProblemCode code,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var document = Document(code, detail, context.Request.Path, extensions);

        context.Response.StatusCode = document.Status!.Value;
        await context.Response.WriteAsJsonAsync(
            document, options: null, ContentType, context.RequestAborted);
    }

    /// <summary>
    /// What turns anything that escaped an endpoint into <c>internal</c> with
    /// nothing else in it. The exception goes to the log, where an operator can
    /// see it; the caller gets a code and a title, because an exception message
    /// is the other place a value could leak into (§6.5).
    /// </summary>
    public sealed class Handler(ILogger<Handler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(
            HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            var document = Document(ProblemCode.Internal, detail: null, context.Request.Path);

            context.Response.StatusCode = document.Status!.Value;
            await context.Response.WriteAsJsonAsync(
                document, options: null, ContentType, cancellationToken);

            return true;
        }
    }
}
