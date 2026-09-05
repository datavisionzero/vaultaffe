using Vaultaffe.Application.Acts;

namespace Vaultaffe.Api.Http;

/// <summary>What the CLI is told when a login begins.</summary>
/// <param name="DeviceCode">The long code the CLI keeps and polls with. It is never shown to a person.</param>
/// <param name="UserCode">The short code the CLI prints, spelled as a person reads it.</param>
/// <param name="VerificationUri">Where a human goes to confirm, relative to this instance.</param>
/// <param name="VerificationUriComplete">The same, with the code already in it.</param>
/// <param name="ExpiresInSeconds">How long the human has.</param>
/// <param name="IntervalSeconds">How often the CLI should poll.</param>
public sealed record DeviceLoginShape(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int ExpiresInSeconds,
    int IntervalSeconds);

/// <summary>The CLI's poll.</summary>
public sealed record RedeemDeviceRequest(string DeviceCode);

/// <summary>
/// The device-code login of Specification §6.2 — the only one that works in an
/// SSH session, a CI job, a container and an agent's sandbox, because it is the
/// only one that needs no browser on the machine doing the asking.
/// </summary>
/// <remarks>
/// Both endpoints are unauthenticated by definition: the whole flow exists to
/// turn no credential into one. What stands in for authentication is that a human
/// on some other machine has to confirm, with their password, within ten minutes.
/// </remarks>
public static class DeviceEndpoints
{
    private const string Tag = "Device login";

    public static IEndpointRouteBuilder MapDeviceLogin(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup($"{ApiVersion.Route}/device").WithTags(Tag);

        api.MapPost("/authorizations", async (
                BeginDeviceLogin begin, CancellationToken cancellation) =>
            {
                var begun = await begin.ExecuteAsync(cancellation);

                return new DeviceLoginShape(
                    begun.DeviceCode,
                    begun.UserCode,
                    begun.VerificationUri,
                    begun.VerificationUriComplete,
                    begun.ExpiresInSeconds,
                    begun.IntervalSeconds);
            })
            .WithName("BeginDeviceLogin")
            .WithSummary("Begin a login: a short code for the human, a long one for the client.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Every state but "approved" is its own refusal code, so that a client
        // keeps polling on exactly one of them and stops on the rest
        // (docs/api.md).
        api.MapPost("/tokens", async (
                RedeemDeviceRequest request,
                RedeemDeviceLogin redeem,
                CancellationToken cancellation) =>
                Shapes.Session(await redeem.ExecuteAsync(request.DeviceCode, cancellation)))
            .WithName("RedeemDeviceLogin")
            .WithSummary("Poll: the session token once a human has confirmed, a code that says why not until then.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
