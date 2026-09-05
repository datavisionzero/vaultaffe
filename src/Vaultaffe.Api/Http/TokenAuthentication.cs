using System.Net.Http.Headers;
using Vaultaffe.Application.Acts;
using Vaultaffe.Domain.Refusals;

namespace Vaultaffe.Api.Http;

/// <summary>
/// Turns <c>Authorization: Bearer &lt;token&gt;</c> into the caller every act and
/// every query filter reads (<c>docs/api.md</c>).
/// </summary>
/// <remarks>
/// A request with no token is let through as nobody: the handshake, the first run
/// and a sign-in all happen without one, and an endpoint that needs a caller
/// refuses on its own. A request with a token that does not authenticate is
/// refused here and does not reach an endpoint — presenting a revoked token and
/// being treated as an anonymous caller is how a client ends up debugging the
/// wrong thing.
/// <para>
/// The three token kinds all arrive the same way. Which one it is, is read from
/// the prefix before anything is looked up (ADR 0004), and it is what the change
/// log records as the identity type (§6.5).
/// </para>
/// </remarks>
public static class TokenAuthentication
{
    public static IApplicationBuilder UseVaultaffeTokens(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var presented = Presented(context.Request);

            if (presented is null)
            {
                await next(context);
                return;
            }

            var caller = await context.RequestServices
                .GetRequiredService<AuthenticateToken>()
                .ExecuteAsync(presented, context.RequestAborted);

            if (caller is null)
            {
                // Deliberately one sentence for four cases — unknown, revoked,
                // expired, malformed. Which one it is, is information about a
                // credential the caller does not hold.
                await Problems.WriteAsync(
                    context,
                    RefusalCode.Unauthenticated,
                    "That token does not authenticate anybody here.");

                return;
            }

            context.RequestServices.GetRequiredService<CallerContext>().Authenticated(caller);

            await next(context);
        });

    private static string? Presented(HttpRequest request) =>
        AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header)
        && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            ? header.Parameter
            : null;
}
