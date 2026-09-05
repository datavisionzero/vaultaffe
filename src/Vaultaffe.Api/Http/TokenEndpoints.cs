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
/// Token management (<c>docs/api.md</c>): create, list, revoke. The value is shown
/// exactly once, in the answer that created it.
/// </summary>
/// <remarks>
/// Creating and revoking are two of the short list only a person may do
/// (Specification §6.4): a token is itself a secret, and one an agent created
/// through the CLI would be printed to stdout and thus into its own context
/// (§6.1). Both say so by declaring it — the enforcement and the refusal are
/// <c>Authority</c>'s, and there is no check in either handler. Listing is not on
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

        api.MapDelete("/{id:guid}", async (
                Guid id, RevokeToken revoke, CancellationToken cancellation) =>
                Shapes.Token(await revoke.ExecuteAsync(id, cancellation)))
            .HumanOnly(HumanAction.RevokeToken)
            .WithName("RevokeToken")
            .WithSummary("Revoke a token. Revoked rather than deleted, so its entries keep an author.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
