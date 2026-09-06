using Vaultaffe.Application.Acts;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Refusals;

namespace Vaultaffe.Api.Http;

/// <summary>
/// Whether this instance has been started, what it is called, and whether the
/// first run needs a claim secret.
/// </summary>
/// <remarks>
/// <c>NeedsClaim</c> is not a leak and is what lets the first-run page put the
/// field on the screen at all. It says that a secret is required, never anything
/// about the secret (ADR 0019).
/// </remarks>
public sealed record InstanceShape(bool Started, string? OrganizationName, bool NeedsClaim);

/// <summary>The first run: the first user, who becomes the administrator.</summary>
public sealed record StartInstanceRequest(string Email, string Name, string Password);

/// <summary>What a first run answers: the organization, and the session that made it.</summary>
public sealed record InstanceStartedShape(
    Guid OrganizationId, string OrganizationName, SessionShape Session);

/// <summary>Email and password, for the one endpoint that takes them.</summary>
public sealed record SignInRequest(string Email, string Password);

/// <summary>
/// Getting in: the first run, a sign-in, and who the caller turned out to be
/// (<c>docs/api.md</c>).
/// </summary>
public static class IdentityEndpoints
{
    private const string Tag = "Identity";

    /// <summary>
    /// Where the first run presents this instance's claim secret. One name, so
    /// that the CLI, the web application and whoever writes a third client all
    /// spell it the same (<c>docs/api.md</c>).
    /// </summary>
    public const string ClaimHeader = "Vaultaffe-Claim";

    public static IEndpointRouteBuilder MapIdentity(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup(ApiVersion.Route).WithTags(Tag);

        // Unauthenticated, because a client asking this has nothing yet: it is
        // how the CLI and the web application tell "you need to start this
        // instance" from "you need to sign in".
        api.MapGet("/instance", async (IIdentityStore identities, CancellationToken cancellation) =>
            {
                var organization = await identities.FindTheOrganizationAsync(cancellation);

                return new InstanceShape(
                    organization is not null, organization?.Name, NeedsClaim: organization is null);
            })
            .WithName("ReadInstance")
            .WithSummary("Whether this instance has been started, and what its organization is called.");

        // The claim secret travels in a header rather than in the body, because it
        // is a credential and not a field of the thing being made: it belongs
        // beside the other credential this API takes on a request, not among the
        // name and the address of the person being created (ADR 0019).
        api.MapPost("/instance", async (
                StartInstanceRequest request,
                StartTheInstance start,
                HttpRequest http,
                CancellationToken cancellation) =>
            {
                var started = await start.ExecuteAsync(
                    request.Email,
                    request.Name,
                    request.Password,
                    http.Headers[ClaimHeader].FirstOrDefault(),
                    cancellation);

                return new InstanceStartedShape(
                    started.OrganizationId,
                    started.OrganizationName,
                    Shapes.Session(started.Session));
            })
            .WithName("StartInstance")
            .WithSummary("The first run: the default organization, and the first user as its administrator.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost("/sessions", async (
                SignInRequest request, SignIn signIn, CancellationToken cancellation) =>
                Shapes.Session(await signIn.ExecuteAsync(
                    request.Email, request.Password, cancellation)))
            .WithName("SignIn")
            .WithSummary("Email and password in, a session token out. The token is shown exactly once.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        api.MapDelete("/sessions/current", async (
                ICallerIdentity identity, RevokeToken revoke, CancellationToken cancellation) =>
            {
                var caller = identity.Required;

                if (!caller.IsHumanSession)
                {
                    throw Refusal.Forbidden(
                        "Only a session can be signed out. A service or agent token is revoked "
                        + "by a human, and outlives the process holding it.");
                }

                return Shapes.Token(await revoke.ExecuteAsync(caller.TokenId, cancellation));
            })
            .WithName("SignOut")
            .WithSummary("Revoke the session this request came in under.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapGet("/me", (ICallerIdentity identity) => Shapes.Me(identity.Required))
            .WithName("ReadMe")
            .WithSummary("The caller: the organization, the person, and the token it came in under.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
