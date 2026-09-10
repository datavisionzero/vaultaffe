using Vaultaffe.Application.Acts;
using Vaultaffe.Domain.Authorization;

namespace Vaultaffe.Api.Http;

/// <summary>A token to create. Everything but the kind and the name is optional.</summary>
/// <param name="Kind"><c>service</c> or <c>agent</c>. A session comes from signing in.</param>
/// <param name="Name">What a human calls it, so that a revocation list is readable.</param>
/// <param name="Scopes">Omitted means the default of that kind: everything for an agent, names and read for a service.</param>
/// <param name="Bindings">Omitted or empty means the whole organization.</param>
/// <param name="ExpiresAt">Omitted means it lives until it is revoked.</param>
public sealed record CreateTokenRequest(
    string Kind,
    string Name,
    IReadOnlyList<string>? Scopes = null,
    IReadOnlyList<BindingShape>? Bindings = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>
/// What may be changed about a token that already exists. Everything is optional
/// and omitted means unchanged.
/// </summary>
/// <param name="Name">What a human calls it. The value is untouched, so nothing holding it has to be told.</param>
/// <param name="Scopes">What it may do from now on.</param>
/// <param name="Bindings">Where it reaches. <c>null</c> leaves it alone; <c>[]</c> asks for the whole organization.</param>
public sealed record ChangeTokenRequest(
    string? Name = null,
    IReadOnlyList<string>? Scopes = null,
    IReadOnlyList<BindingShape>? Bindings = null);

/// <summary>
/// Token management (<c>docs/api.md</c>): create, list, change, revoke, purge. The
/// value is shown exactly once, in the answer that created it, and no act here
/// ever produces a second one.
/// </summary>
/// <remarks>
/// Everything but listing is on the short list only a person may do
/// (Specification §6.4). Creating, because a token is itself a secret and one an
/// agent created through the CLI would be printed to stdout and thus into its own
/// context (§6.1); revoking, because that is how a human takes a credential back;
/// changing, because widening one hands a wider credential to whoever already
/// holds that value; and purging, because it is the one removal that cannot be
/// undone. Each says so by declaring it — the enforcement and the refusal are
/// <c>Authority</c>'s, and there is no check in any handler. Listing is not on
/// that list: a revocation list an agent cannot read is not one.
/// </remarks>
public static class TokenEndpoints
{
    private const string Tag = "Tokens";

    public static IEndpointRouteBuilder MapTokens(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup($"{ApiVersion.Route}/tokens").WithTags(Tag);

        api.MapPost("", async (
                CreateTokenRequest request, CreateToken create, CancellationToken cancellation) =>
            {
                var issued = await create.ExecuteAsync(
                    Shapes.KindOf(request.Kind),
                    request.Name,
                    Shapes.ScopesOf(request.Scopes),
                    [.. (request.Bindings ?? []).Select(binding =>
                        new BindingRequest(binding.ProjectId, binding.EnvironmentId))],
                    request.ExpiresAt,
                    cancellation);

                return new TokenIssuedShape(Shapes.Token(issued.Token), issued.Value);
            })
            .HumanOnly(HumanAction.CreateToken)
            .WithName("CreateToken")
            .WithSummary("Create a service or agent token. Its value appears here and nowhere else, ever.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapGet("", async (ListTokens list, CancellationToken cancellation) =>
                (await list.ExecuteAsync(cancellation)).Select(Shapes.Token).ToList())
            .WithName("ReadTokens")
            .WithSummary("Every token of this organization, newest first, revoked ones included.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        api.MapPatch("/{id:guid}", async (
                Guid id,
                ChangeTokenRequest request,
                ChangeToken change,
                CancellationToken cancellation) =>
                Shapes.Token(await change.ExecuteAsync(
                    id,
                    request.Name,
                    Shapes.ScopesOf(request.Scopes),
                    request.Bindings is null
                        ? null
                        : [.. request.Bindings.Select(binding =>
                            new BindingRequest(binding.ProjectId, binding.EnvironmentId))],
                    cancellation)))
            .HumanOnly(HumanAction.ChangeToken)
            .WithName("ChangeToken")
            .WithSummary("Rename a token, or change what it may do and how far it reaches. Its value is untouched.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapDelete("/{id:guid}", async (
                Guid id, RevokeToken revoke, CancellationToken cancellation) =>
                Shapes.Token(await revoke.ExecuteAsync(id, cancellation)))
            .HumanOnly(HumanAction.RevokeToken)
            .WithName("RevokeToken")
            .WithSummary("Revoke a token. Revoked rather than deleted, so its entries keep an author.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/{id:guid}/purge", async (
                Guid id, PurgeToken purge, CancellationToken cancellation) =>
                Results.Ok(Shapes.Token(await purge.ExecuteAsync(id, cancellation))))
            .HumanOnly(HumanAction.PurgeToken)
            .WithName("PurgeToken")
            .WithSummary("Remove a revoked token's row for good. The change log keeps what it signed.")
            .Produces<TokenShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
