using Vaultaffe.Application.Acts;
using Vaultaffe.Domain.Authorization;
using Vaultaffe.Domain.Identities;

namespace Vaultaffe.Api.Http;

/// <summary>A person of the organization. Never a password hash, and never a token.</summary>
public sealed record UserShape(
    Guid Id,
    string Email,
    string Name,
    bool IsAdministrator,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeactivatedAt);

/// <summary>Somebody to invite: an address, a name, and whether they will administer.</summary>
public sealed record InviteUserRequest(string Email, string Name, bool IsAdministrator = false);

/// <summary>An administrator's reset: the password they will hand over.</summary>
public sealed record ResetPasswordRequest(string Password);

/// <summary>The address somebody will sign in with from now on.</summary>
public sealed record ChangeEmailRequest(string Email);

/// <summary>An invitation as every listing shows it — everything but the code.</summary>
public sealed record InvitationShape(
    Guid Id,
    string Email,
    string Name,
    bool IsAdministrator,
    string State,
    Guid InvitedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// An invitation as it is written: the row, and the link nobody will see again.
/// </summary>
/// <param name="Link">
/// Relative to this instance, with the code in the fragment — where the CLI's
/// verification URI is relative for the same reason: the instance does not know
/// what address a browser reached it at, and the client that has one composes it.
/// </param>
public sealed record InvitationWrittenShape(InvitationShape Invitation, string Link);

/// <summary>What a link turns out to be, for whoever is holding it.</summary>
public sealed record InvitationOfferShape(
    string OrganizationName, string Email, string Name, string State);

/// <summary>The code in a link, in a body rather than in a path.</summary>
public sealed record InvitationOfferRequest(string Code);

/// <summary>Accepting one: the code, the name they want to be called, their password.</summary>
public sealed record AcceptInvitationRequest(string Code, string? Name, string Password);

/// <summary>
/// The people of the organization, and the links that invite them
/// (<c>docs/api.md</c>).
/// </summary>
/// <remarks>
/// <b>The instance sends no email</b> (Specification §6.1). An invitation is a
/// link an administrator copies and hands over, and a password reset is an
/// administrator setting one — which is the price of having no external
/// dependency to operate (§4) and, for teams of this size, the right one.
/// <para>
/// Everything here that changes something is human-only and an administrator's:
/// who is in this organization is a decision about people (§6.4). Two endpoints
/// are neither, and they are the two an invited person calls before they have an
/// identity — reading what a link is for, and accepting it. Their credential is
/// the code, which travels in a <b>body</b> and never in a path or a query, so
/// that a working invitation link cannot end up in an access log.
/// </para>
/// </remarks>
public static class UserEndpoints
{
    private const string Tag = "People";

    /// <summary>Where the web application takes somebody holding a link.</summary>
    public const string InvitationPath = "/invite";

    public static IEndpointRouteBuilder MapUsers(this IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup($"{ApiVersion.Route}/users").WithTags(Tag);

        users.MapGet("", async (ListUsers list, CancellationToken cancellation) =>
                (await list.ExecuteAsync(cancellation)).Select(Shape).ToList())
            .HumanOnly(HumanAction.AdministerOrganization)
            .WithName("ReadUsers")
            .WithSummary("The people of this organization, oldest first, deactivated ones included.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        users.MapPost("/{id:guid}/password", async (
                Guid id,
                ResetPasswordRequest request,
                ResetPassword reset,
                CancellationToken cancellation) =>
                Shape(await reset.ExecuteAsync(id, request.Password, cancellation)))
            .AdministratorOnly()
            .WithName("ResetPassword")
            .WithSummary("An administrator sets somebody's password, and ends their sessions.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Not `PATCH /users/{id}`, because there is one field and it is the login
        // name: an endpoint that says what it does is worth more here than a
        // general one that could later do several things at once.
        users.MapPost("/{id:guid}/email", async (
                Guid id,
                ChangeEmailRequest request,
                ChangeEmail change,
                CancellationToken cancellation) =>
                Shape(await change.ExecuteAsync(id, request.Email, cancellation)))
            .AdministratorOnly()
            .WithName("ChangeEmail")
            .WithSummary("An administrator changes the address somebody signs in with. Their sessions stay.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPost("/{id:guid}/deactivate", async (
                Guid id, DeactivateUser deactivate, CancellationToken cancellation) =>
                Shape(await deactivate.ExecuteAsync(id, cancellation)))
            .AdministratorOnly()
            .WithName("DeactivateUser")
            .WithSummary("Take somebody out of the organization. Every token of theirs stops working.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{id:guid}/reactivate", async (
                Guid id, ReactivateUser reactivate, CancellationToken cancellation) =>
                Shape(await reactivate.ExecuteAsync(id, cancellation)))
            .AdministratorOnly()
            .WithName("ReactivateUser")
            .WithSummary("Put them back. Their tokens work again, the revoked ones excepted.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var invitations = endpoints.MapGroup($"{ApiVersion.Route}/invitations").WithTags(Tag);

        invitations.MapPost("", async (
                InviteUserRequest request, InviteUser invite, CancellationToken cancellation) =>
            {
                var written = await invite.ExecuteAsync(
                    request.Email, request.Name, request.IsAdministrator, cancellation);

                return new InvitationWrittenShape(
                    Shape(written.Invitation),
                    $"{InvitationPath}#{written.Code}");
            })
            .AdministratorOnly()
            .WithName("InviteUser")
            .WithSummary("Write out an invitation. Its link appears here and nowhere else, ever.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        invitations.MapGet("", async (ListInvitations list, CancellationToken cancellation) =>
                (await list.ExecuteAsync(cancellation)).Select(Shape).ToList())
            .AdministratorOnly()
            .WithName("ReadInvitations")
            .WithSummary("Every invitation of this organization, newest first, in whatever state.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        invitations.MapDelete("/{id:guid}", async (
                Guid id, WithdrawInvitation withdraw, CancellationToken cancellation) =>
                Shape(await withdraw.ExecuteAsync(id, cancellation)))
            .AdministratorOnly()
            .WithName("WithdrawInvitation")
            .WithSummary("Take one back before anybody used it. The link stops working.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // The two below are what somebody holding a link calls, and they carry no
        // token because that person has no identity here yet — accepting is what
        // gives them one. The code is their credential and it is in the body.
        invitations.MapPost("/offer", async (
                InvitationOfferRequest request,
                ReadInvitationOffer offer,
                CancellationToken cancellation) =>
            {
                var read = await offer.ExecuteAsync(request.Code, cancellation);

                return new InvitationOfferShape(
                    read.OrganizationName, read.Email, read.Name, StateOf(read.State));
            })
            .WithName("ReadInvitationOffer")
            .WithSummary("What an invitation link is for. Unauthenticated: its holder has no identity yet.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        invitations.MapPost("/acceptance", async (
                AcceptInvitationRequest request,
                AcceptInvitation accept,
                CancellationToken cancellation) =>
                Shapes.Session(await accept.ExecuteAsync(
                    request.Code, request.Name, request.Password, cancellation)))
            .WithName("AcceptInvitation")
            .WithSummary("Accept one: a password, and the session it signs you into.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static UserShape Shape(UserRow row) =>
        new(row.Id, row.Email, row.Name, row.IsAdministrator, row.CreatedAt, row.DeactivatedAt);

    private static InvitationShape Shape(InvitationRow row) =>
        new(
            row.Id,
            row.Email,
            row.Name,
            row.IsAdministrator,
            StateOf(row.State),
            row.InvitedByUserId,
            row.CreatedAt,
            row.ExpiresAt);

    /// <summary>The wire spelling of a state, which is the word itself.</summary>
    private static string StateOf(InvitationState state) => state switch
    {
        InvitationState.Open => "open",
        InvitationState.Accepted => "accepted",
        InvitationState.Withdrawn => "withdrawn",
        InvitationState.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(
            nameof(state), state, "An invitation state without a name."),
    };
}
