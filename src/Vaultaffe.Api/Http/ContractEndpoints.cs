using Vaultaffe.Api.Hosting;

namespace Vaultaffe.Api.Http;

/// <summary>What <c>GET /api/handshake</c> answers.</summary>
/// <param name="Product">Always <c>vaultaffe</c>. A client pointed at the wrong host should find that out here.</param>
/// <param name="Release">The release this instance is — not the version of the API.</param>
/// <param name="ApiVersions">Every version of the contract this instance serves, newest last.</param>
/// <param name="MinimumClient">The oldest client release this instance still answers.</param>
public sealed record Handshake(
    string Product,
    string Release,
    IReadOnlyList<string> ApiVersions,
    string MinimumClient);

/// <summary>One refusal this instance can make.</summary>
/// <param name="Code">What a client switches on.</param>
/// <param name="Status">The HTTP status it arrives with.</param>
/// <param name="Title">What it means, in one line and never with a value in it.</param>
public sealed record ProblemKind(string Code, int Status, string Title);

/// <summary>
/// The contract talking about itself: what this instance is, what it serves, and
/// what it can refuse. Everything here is outside the versioned prefix and
/// outside authentication, because all of it is what a client reads before it
/// knows whether either applies to it.
/// </summary>
public static class ContractEndpoints
{
    /// <summary>
    /// What the operations of this file are grouped under in the document. A
    /// generated client reads the tag as a namespace, so it is named after what
    /// these endpoints are about rather than after the assembly they live in.
    /// </summary>
    private const string Tag = "Contract";

    public static IEndpointRouteBuilder MapContract(this IEndpointRouteBuilder endpoints)
    {
        // Deliberately not under /api/v1: this is the request a client makes
        // before it knows which versions exist, and a handshake that needed one
        // would be a chicken with no egg.
        endpoints.MapGet("/api/handshake", () => new Handshake(
                Product: "vaultaffe",
                Release: InstanceVersion.Value,
                ApiVersions: ApiVersion.Supported,
                MinimumClient: ClientVersion.Minimum.ToString()))
            .WithTags(Tag)
            .WithName("Handshake")
            .WithSummary("What this instance is and which versions of the contract it serves.");

        endpoints.MapGet("/problems", () => ProblemCatalogue)
            .WithTags(Tag)
            .WithName("ReadProblems")
            .WithSummary("Every refusal this instance can make, by code.");

        // The `type` of a problem document is /problems/<code>, and RFC 9457
        // resolves it against the request — so it points here. Serving it is
        // what keeps that from being a URI that only looks dereferenceable.
        endpoints.MapGet("/problems/{code}", (string code) =>
                Problems.Parse(code) is { } known
                    ? Results.Ok(Describe(known))
                    : Problems.Result(
                        ProblemCode.NotFound, "This instance does not raise a problem by that code."))
            .WithTags(Tag)
            .WithName("ReadProblem")
            .WithSummary("One refusal, by code.")
            .Produces<ProblemKind>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // A client that asked for a version this build does not serve gets told
        // that, rather than a 404 it has to interpret as "wrong path, wrong
        // version, or wrong server". A literal route always wins over this one,
        // so it only ever answers for paths no endpoint claimed.
        endpoints.Map("/api/{version}/{**rest}", (string version) =>
                ApiVersion.Serves(version)
                    ? Problems.Result(ProblemCode.NotFound, "No endpoint at this path.")
                    : Problems.Result(
                        ProblemCode.UnsupportedApiVersion,
                        $"This instance serves {string.Join(", ", ApiVersion.Supported)}.",
                        new Dictionary<string, object?> { ["apiVersions"] = ApiVersion.Supported }))
            .ExcludeFromDescription();

        return endpoints;
    }

    private static IReadOnlyList<ProblemKind> ProblemCatalogue { get; } =
        [.. Enum.GetValues<ProblemCode>().Select(Describe)];

    private static ProblemKind Describe(ProblemCode code) =>
        new(Problems.CodeOf(code), Problems.StatusOf(code), Problems.TitleOf(code));
}
