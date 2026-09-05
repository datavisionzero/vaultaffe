using Vaultaffe.Application.Acts;

namespace Vaultaffe.Api.Http;

/// <summary>The organization the caller is in. One in the MVP, called Default.</summary>
public sealed record OrganizationShape(Guid Id, string Name, DateTimeOffset CreatedAt);

/// <summary>A new name — for the organization, or for the person asking.</summary>
public sealed record NameRequest(string Name);

/// <summary>Changing your own password: the one you have, and the one you want.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// The organization, and the caller's own profile (<c>docs/api.md</c>).
/// </summary>
/// <remarks>
/// Two things a screen needs and nothing else did until it existed: the name of
/// the organization, which its header shows, and the two things a person changes
/// about themselves.
/// <para>
/// Renaming the organization is an administrator's and declares it. The profile
/// endpoints declare nothing and refuse a token inside the act instead: changing
/// your own name is not one of the short list of §6.4 — it is nobody's
/// administration but your own — and what is refused there is a service or agent
/// token acting for the person accountable for it, exactly as signing out is.
/// </para>
/// </remarks>
public static class OrganizationEndpoints
{
    private const string Tag = "Organization";

    public static IEndpointRouteBuilder MapOrganization(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup(ApiVersion.Route).WithTags(Tag);

        api.MapGet("/organization", async (
                ReadOrganization read, CancellationToken cancellation) =>
                Shape(await read.ExecuteAsync(cancellation)))
            .WithName("ReadOrganization")
            .WithSummary("The organization this caller is in.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        api.MapPatch("/organization", async (
                NameRequest request, RenameOrganization rename, CancellationToken cancellation) =>
                Shape(await rename.ExecuteAsync(request.Name, cancellation)))
            .AdministratorOnly()
            .WithName("RenameOrganization")
            .WithSummary("Rename it. The name is a team's own word for itself and nothing depends on it.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPatch("/me", async (
                NameRequest request, RenameMyself rename, CancellationToken cancellation) =>
            {
                var row = await rename.ExecuteAsync(request.Name, cancellation);

                return new UserShape(
                    row.Id, row.Email, row.Name, row.IsAdministrator, row.CreatedAt, row.DeactivatedAt);
            })
            .WithName("RenameMyself")
            .WithSummary("Change your own name — what the change log calls you.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("/me/password", async (
                ChangePasswordRequest request,
                ChangeMyPassword change,
                CancellationToken cancellation) =>
            {
                await change.ExecuteAsync(
                    request.CurrentPassword, request.NewPassword, cancellation);

                return Results.NoContent();
            })
            .WithName("ChangeMyPassword")
            .WithSummary("Change your own password. Every other session of yours ends with it.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static OrganizationShape Shape(OrganizationRow row) =>
        new(row.Id, row.Name, row.CreatedAt);
}
