using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Vaultaffe.Domain.Refusals;

namespace Vaultaffe.Api.Http;

/// <summary>
/// The one place a refusal becomes a problem document (<c>docs/api.md</c>): every
/// error this API returns is <c>application/problem+json</c> with a stable
/// <c>type</c> whose last segment is the code a client switches on, and that same
/// code as a member beside it.
/// </summary>
/// <remarks>
/// The status of a code lives here and nowhere else — not in Domain, where the
/// codes live. HTTP's word for a refusal is the status; the CLI's word for it is
/// the code and an exit code of its own, and neither has to be derivable from the
/// other.
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

    /// <summary>The wire spelling: <see cref="RefusalCode.ClientTooOld"/> is <c>client-too-old</c>.</summary>
    public static string CodeOf(RefusalCode code) =>
        JsonNamingPolicy.KebabCaseLower.ConvertName(code.ToString());

    /// <summary>The code again, spelled as the <c>type</c> a client may dereference.</summary>
    public static string TypeOf(RefusalCode code) => TypePrefix + CodeOf(code);

    /// <summary>Which code a wire spelling names, or null when this build has no such code.</summary>
    public static RefusalCode? Parse(string? code) =>
        Enum.GetValues<RefusalCode>().Cast<RefusalCode?>()
            .FirstOrDefault(known => CodeOf(known!.Value) == code);

    public static int StatusOf(RefusalCode code) => code switch
    {
        RefusalCode.Validation or RefusalCode.ClientVersionUnreadable
            or RefusalCode.DevicePending or RefusalCode.DeviceDenied or RefusalCode.DeviceExpired =>
            StatusCodes.Status400BadRequest,
        RefusalCode.Unauthenticated => StatusCodes.Status401Unauthorized,
        RefusalCode.Forbidden or RefusalCode.HumanOnly or RefusalCode.InsufficientScope
            or RefusalCode.OutOfReach =>
            StatusCodes.Status403Forbidden,
        RefusalCode.NotFound or RefusalCode.UnsupportedApiVersion => StatusCodes.Status404NotFound,
        RefusalCode.AlreadyStarted or RefusalCode.NameTaken => StatusCodes.Status409Conflict,
        RefusalCode.NotRecoverable => StatusCodes.Status410Gone,
        RefusalCode.ClientTooOld => StatusCodes.Status426UpgradeRequired,
        RefusalCode.Internal => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A refusal code without a status."),
    };

    public static string TitleOf(RefusalCode code) => code switch
    {
        RefusalCode.Validation => "A field is missing, malformed or over its limit",
        RefusalCode.NotFound => "Nothing by that name",
        RefusalCode.Unauthenticated => "No token, an unknown token, a revoked one, or the wrong password",
        RefusalCode.Forbidden => "This identity may not do this",
        RefusalCode.HumanOnly => "This action is reserved for a person",
        RefusalCode.InsufficientScope => "This token does not carry the scope this needs",
        RefusalCode.OutOfReach => "This token is not bound to that project or environment",
        RefusalCode.AlreadyStarted => "This instance already has its first user",
        RefusalCode.NameTaken => "Something of that name is already here",
        RefusalCode.NotRecoverable => "That was deleted longer ago than the recovery window",
        RefusalCode.DevicePending => "Nobody has confirmed this login yet",
        RefusalCode.DeviceDenied => "A human refused this login",
        RefusalCode.DeviceExpired => "This login expired, or its token was already collected",
        RefusalCode.UnsupportedApiVersion => "This instance does not serve that version of the API",
        RefusalCode.ClientTooOld => "This client is older than this instance accepts",
        RefusalCode.ClientVersionUnreadable => "The announced client version is not a version",
        RefusalCode.Internal => "Something went wrong on the server",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A refusal code without a title."),
    };

    /// <summary>The document for <paramref name="code"/>.</summary>
    public static ProblemDetails Document(
        RefusalCode code,
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
        RefusalCode code,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null) =>
        Results.Problem(Document(code, detail, instance: null, extensions));

    /// <summary>
    /// Writes the document straight to the response, for the refusals that
    /// happen before an endpoint runs.
    /// </summary>
    public static async Task WriteAsync(
        HttpContext context,
        RefusalCode code,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var document = Document(code, detail, context.Request.Path, extensions);

        context.Response.StatusCode = document.Status!.Value;
        await context.Response.WriteAsJsonAsync(
            document, options: null, ContentType, context.RequestAborted);
    }

    /// <summary>
    /// What turns a <see cref="Refusal"/> thrown by an act into its document, and
    /// anything else into <c>internal</c> with nothing else in it. The exception
    /// goes to the log, where an operator can see it; the caller gets a code and
    /// a title, because an exception message is the other place a value could
    /// leak into (§6.5).
    /// </summary>
    public sealed class Handler(ILogger<Handler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(
            HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            var document = exception is Refusal refusal
                ? Document(refusal.Code, refusal.Detail, context.Request.Path, refusal.Extensions)
                : Document(RefusalCode.Internal, detail: null, context.Request.Path);

            if (document.Status == StatusCodes.Status500InternalServerError)
            {
                logger.LogError(
                    exception,
                    "Unhandled exception on {Method} {Path}.",
                    context.Request.Method,
                    context.Request.Path);
            }

            context.Response.StatusCode = document.Status!.Value;
            await context.Response.WriteAsJsonAsync(
                document, options: null, ContentType, cancellationToken);

            return true;
        }
    }
}
