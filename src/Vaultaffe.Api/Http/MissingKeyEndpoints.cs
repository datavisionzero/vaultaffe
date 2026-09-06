using Vaultaffe.Application.Acts;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Api.Http;

/// <summary>
/// A key this environment has not got, the environments that have it, and when
/// somebody told the notice to stop saying so.
/// </summary>
public sealed record MissingKeyShape(
    string Name, IReadOnlyList<string> PresentIn, DateTimeOffset? DismissedAt);

/// <summary>
/// The missing-key notice (Specification §6.1) — <b>a display, and nothing that
/// acts</b>.
/// </summary>
/// <remarks>
/// There is no endpoint here that creates a secret, and that is the design rather
/// than an omission: Doppler does not put a new key into every environment by
/// itself either, and a suggestion that writes on its own is exactly the wrong
/// convenience in a secrets manager. What the notice offers instead is the one
/// thing §6.1 asks for — the ability to say "not here", per key and per
/// environment, and to take that back.
/// <para>
/// Reading it needs <c>names</c> and nothing more: it says which keys exist
/// where, which is what that scope is for. Dismissing needs <c>write</c>, because
/// it changes what everybody in the organization sees — and it reaches only the
/// environments the caller's binding already covers, so no notice tells a token
/// about a place it may not touch.
/// </para>
/// </remarks>
public static class MissingKeyEndpoints
{
    private const string Tag = "Secrets";

    private const string Under = "/{project}/environments/{environment}/missing";

    public static IEndpointRouteBuilder MapMissingKeys(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup($"{ApiVersion.Route}/projects").WithTags(Tag);

        api.MapGet(Under, async (
                string project,
                string environment,
                ReadMissingKeys read,
                CancellationToken cancellation) =>
                (await read.ExecuteAsync(project, environment, cancellation))
                    .Select(Shape).ToList())
            .Needing(Scopes.Names)
            .WithName("ReadMissingKeys")
            .WithSummary(
                "Keys most of this project's other environments have and this one has not. "
                + "A line somebody silenced carries the moment they did; it is not left out.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost($"{Under}/{{name}}/dismissal", async (
                string project,
                string environment,
                string name,
                DismissMissingKey dismiss,
                CancellationToken cancellation) =>
                Results.Ok(Shape(await dismiss.ExecuteAsync(
                    project, environment, name, cancellation))))
            .Needing(Scopes.Write)
            .WithName("DismissMissingKey")
            .WithSummary("Stop the notice mentioning that key here. Nothing is created.")
            .Produces<MissingKeyShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapDelete($"{Under}/{{name}}/dismissal", async (
                string project,
                string environment,
                string name,
                WithdrawDismissal withdraw,
                CancellationToken cancellation) =>
                Shape(await withdraw.ExecuteAsync(project, environment, name, cancellation)))
            .Needing(Scopes.Write)
            .WithName("WithdrawDismissal")
            .WithSummary("Mention that key here again.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static MissingKeyShape Shape(MissingKeyRow row) =>
        new(row.Name, row.PresentIn, row.DismissedAt);
}
