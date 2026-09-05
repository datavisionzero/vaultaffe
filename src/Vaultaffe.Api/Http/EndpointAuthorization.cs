using Vaultaffe.Application.Authorization;
using Vaultaffe.Domain.Authorization;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Api.Http;

/// <summary>
/// What an endpoint needs of its caller, as metadata on the endpoint rather than
/// as code inside it.
/// </summary>
/// <param name="Scopes">Every scope the token has to carry, or null when the endpoint needs none.</param>
/// <param name="HumanAction">The human-only action this is, or null when an agent may do it.</param>
public sealed record Needs(Scopes? Scopes = null, HumanAction? HumanAction = null);

/// <summary>
/// The authorization of Specification §6.4 as one middleware over every endpoint.
/// </summary>
/// <remarks>
/// <b>An endpoint declares, it does not check.</b> `.HumanOnly(…)` and
/// `.Needing(…)` put a <see cref="Needs"/> on the endpoint; this middleware reads
/// it after routing and hands it to <see cref="Authority"/>, which is where the
/// rules and the refusals actually live. A handler therefore has no authorization
/// code in it at all, which is the point: the rule is in one place, and an
/// endpoint that forgot to declare its requirement is a missing line one can grep
/// for rather than a check hidden in the middle of a method.
/// <para>
/// What cannot be declared here is the binding, because it depends on which
/// project the request names, and that is the act's business — so the act asks
/// <see cref="Authority"/> the same question through the same object. There is one
/// implementation either way.
/// </para>
/// </remarks>
public static class EndpointAuthorization
{
    /// <summary>This endpoint is one of the short list only a person may do (§6.4).</summary>
    public static TBuilder HumanOnly<TBuilder>(this TBuilder builder, HumanAction action)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new Needs(HumanAction: action));

    /// <summary>This endpoint needs every scope in <paramref name="scopes"/>.</summary>
    public static TBuilder Needing<TBuilder>(this TBuilder builder, Scopes scopes)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new Needs(Scopes: scopes));

    /// <summary>
    /// Enforce what each endpoint declared. Sits after routing, so that the
    /// endpoint — and therefore its metadata — is known, and before the endpoint
    /// runs, so that nothing it would have done happens first.
    /// </summary>
    public static IApplicationBuilder UseVaultaffeAuthorization(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<Needs>() is { } needs)
            {
                var authority = context.RequestServices.GetRequiredService<Authority>();

                if (needs.HumanAction is { } action)
                {
                    authority.RequiresAHuman(action);
                }

                if (needs.Scopes is { } scopes)
                {
                    authority.Requires(scopes);
                }
            }

            await next(context);
        });
}
