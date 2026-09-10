using Vaultaffe.Application.Acts;
using Vaultaffe.Domain.Authorization;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Refusals;

namespace Vaultaffe.Api.Http;

/// <summary>What a machine says when it asks for a token of its own.</summary>
/// <param name="Name">What it calls itself, for the person who will decide. Believed by nobody.</param>
public sealed record BeginEnrollmentRequest(string Name);

/// <summary>What the asking machine is told, so that it can print a code and wait.</summary>
/// <param name="DeviceCode">The long code the client keeps and polls with. Never shown to a person.</param>
/// <param name="UserCode">The short code the client prints, spelled as a person reads it.</param>
/// <param name="VerificationUri">Where a person goes to decide, relative to this instance.</param>
/// <param name="VerificationUriComplete">The same, with the code already in it.</param>
/// <param name="ExpiresInSeconds">How long the person has.</param>
/// <param name="IntervalSeconds">How often the client should poll.</param>
public sealed record EnrollmentShape(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int ExpiresInSeconds,
    int IntervalSeconds);

/// <summary>What a person is being asked to agree to.</summary>
/// <param name="RequestedName">What the asking client calls itself. The instance cannot check it.</param>
/// <param name="AskedAt">When it asked.</param>
/// <param name="ExpiresAt">When the code stops being worth anything.</param>
/// <param name="State"><c>pending</c>, <c>approved</c>, <c>denied</c>, <c>expired</c> or <c>redeemed</c>.</param>
public sealed record EnrollmentAskedShape(
    string RequestedName,
    DateTimeOffset AskedAt,
    DateTimeOffset ExpiresAt,
    string State);

/// <summary>What a person agrees to. Everything but the name is optional.</summary>
/// <param name="Name">What it is called from here on. Omitted keeps what the client asked for.</param>
/// <param name="Scopes">Omitted means everything, which is what an agent token gets.</param>
/// <param name="Bindings">Omitted or empty means the whole organization.</param>
public sealed record ApproveEnrollmentRequest(
    string? Name = null,
    IReadOnlyList<string>? Scopes = null,
    IReadOnlyList<BindingShape>? Bindings = null);

/// <summary>The asking machine's poll.</summary>
public sealed record CollectEnrollmentRequest(string DeviceCode);

/// <summary>
/// An agent asks for a token of its own
/// (<c>docs/adr/0021-an-agent-asks-for-its-own-token.md</c>): the device-code
/// flow of <c>/device</c>, ending in an agent token rather than in a person's
/// session.
/// </summary>
/// <remarks>
/// <b>Two of these are unauthenticated by definition</b> — beginning and
/// collecting — because the whole flow exists to turn no credential into one.
/// What stands in for authentication is a person, signed in on some other
/// machine, agreeing within ten minutes.
/// <para>
/// <b>Agreeing is human-only</b>, with <c>create-token</c>: what comes out of it
/// is a token, and one an agent could agree to for itself would be a credential
/// nobody issued. Reading and refusing need only a session — a person who cannot
/// agree to something should still be able to see what is being asked and say
/// no to it.
/// </para>
/// </remarks>
public static class EnrollmentEndpoints
{
    private const string Tag = "Enrollment";

    public static IEndpointRouteBuilder MapEnrollments(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup($"{ApiVersion.Route}/enrollments").WithTags(Tag);

        api.MapPost("", async (
                BeginEnrollmentRequest request,
                BeginEnrollment begin,
                CancellationToken cancellation) =>
            {
                var begun = await begin.ExecuteAsync(request.Name, cancellation);

                return new EnrollmentShape(
                    begun.DeviceCode,
                    begun.UserCode,
                    begun.VerificationUri,
                    begun.VerificationUriComplete,
                    begun.ExpiresInSeconds,
                    begun.IntervalSeconds);
            })
            .WithName("BeginEnrollment")
            .WithSummary("Ask for a token of this machine's own: a short code for a person, a long one for the client.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/tokens", async (
                CollectEnrollmentRequest request,
                CollectEnrollment collect,
                CancellationToken cancellation) =>
            {
                var issued = await collect.ExecuteAsync(request.DeviceCode, cancellation);

                return new TokenIssuedShape(Shapes.Token(issued.Token), issued.Value);
            })
            .WithName("CollectEnrollment")
            .WithSummary("Poll: the token once a person has agreed, a code that says why not until then.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/{code}", async (
                string code, ReadEnrollment read, CancellationToken cancellation) =>
                Asked(await read.ExecuteAsync(code, cancellation)))
            .WithName("ReadEnrollment")
            .WithSummary("What a machine is asking for, for the person about to decide.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/{code}/approval", async (
                string code,
                ApproveEnrollmentRequest request,
                ApproveEnrollment approve,
                CancellationToken cancellation) =>
                Asked(await approve.ExecuteAsync(
                    code,
                    request.Name,
                    Shapes.ScopesOf(request.Scopes),
                    [.. (request.Bindings ?? []).Select(binding =>
                        new BindingRequest(binding.ProjectId, binding.EnvironmentId))],
                    cancellation)))
            .HumanOnly(HumanAction.CreateToken)
            .WithName("ApproveEnrollment")
            .WithSummary("Agree to it, and say what the token may do and how far it reaches.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/{code}/refusal", async (
                string code, RefuseEnrollment refuse, CancellationToken cancellation) =>
                Asked(await refuse.ExecuteAsync(code, cancellation)))
            .WithName("RefuseEnrollment")
            .WithSummary("Say no: nothing on this machine asked for a token.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static EnrollmentAskedShape Asked(EnrollmentAsked asked) =>
        new(asked.RequestedName, asked.AskedAt, asked.ExpiresAt, StateOf(asked.State));

    /// <summary>
    /// The state as a client reads it. The same five words the refusal codes of a
    /// poll are named after, so that a screen and a CLI describe one enrollment
    /// the same way.
    /// </summary>
    private static string StateOf(DeviceAuthorizationState state) => state switch
    {
        DeviceAuthorizationState.Pending => "pending",
        DeviceAuthorizationState.Approved => "approved",
        DeviceAuthorizationState.Denied => "denied",
        DeviceAuthorizationState.Expired => "expired",
        DeviceAuthorizationState.Redeemed => "redeemed",
        _ => throw new ArgumentOutOfRangeException(
            nameof(state), state, "An enrollment state without a name."),
    };
}
