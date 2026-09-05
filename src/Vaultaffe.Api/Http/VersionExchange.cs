using Vaultaffe.Api.Hosting;

namespace Vaultaffe.Api.Http;

/// <summary>
/// The version exchange of Specification §6.3, as it happens on every request:
/// the client announces its release in <c>Vaultaffe-Client</c>, the instance
/// announces its own in <c>Vaultaffe-Version</c>, and a client too old to be
/// served is told so instead of being let through to fail at a field.
/// </summary>
/// <remarks>
/// The response header is on every answer, the refused and the failed ones
/// included: a client reports skew from whatever it got back, and a 401 is an
/// answer. The request header is optional — a browser has none and needs none;
/// what is not optional is that a header which is there means something.
/// </remarks>
public static class VersionExchange
{
    public static IApplicationBuilder UseVaultaffeVersionExchange(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[InstanceVersion.Header] = InstanceVersion.Value;
                return Task.CompletedTask;
            });

            var announced = context.Request.Headers[ClientVersion.Header].ToString();

            if (string.IsNullOrEmpty(announced))
            {
                await next(context);
                return;
            }

            if (!ClientVersion.TryParse(announced, out var client))
            {
                await Problems.WriteAsync(
                    context,
                    ProblemCode.ClientVersionUnreadable,
                    $"'{ClientVersion.Header}' carries a release such as 1.4.0, not '{announced}'.");

                return;
            }

            if (!client.IsServed)
            {
                await Problems.WriteAsync(
                    context,
                    ProblemCode.ClientTooOld,
                    $"This instance serves clients from {ClientVersion.Minimum} onwards.",
                    new Dictionary<string, object?>
                    {
                        ["client"] = client.ToString(),
                        ["minimumClient"] = ClientVersion.Minimum.ToString(),
                    });

                return;
            }

            await next(context);
        });
}
