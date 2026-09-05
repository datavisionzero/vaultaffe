using Vaultaffe.Application.Acts;
using Vaultaffe.Domain.Authorization;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Api.Http;

/// <summary>A project to create.</summary>
/// <param name="Name">Lower-case, as a <c>vaultaffe://</c> reference carries it (ADR 0003).</param>
/// <param name="Environments">Omitted means the defaults; an empty list means none.</param>
public sealed record CreateProjectRequest(string Name, IReadOnlyList<string>? Environments = null);

/// <summary>An environment to create in a project.</summary>
public sealed record CreateEnvironmentRequest(string Name);

/// <summary>A new name for something that already exists.</summary>
public sealed record RenameRequest(string Name);

/// <summary>An environment as every listing shows it.</summary>
public sealed record EnvironmentShape(
    Guid Id,
    Guid ProjectId,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

/// <summary>A project, and the environments the caller may see of it.</summary>
public sealed record ProjectShape(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<EnvironmentShape> Environments);

/// <summary>
/// Projects and environments (<c>docs/api.md</c>): create, list, rename, delete
/// recoverably, restore — at both levels.
/// </summary>
/// <remarks>
/// <b>Addressed by name.</b> A name is what a person types, what a
/// <c>vaultaffe://</c> reference carries and what the CLI's directory binding
/// holds (Specification §5), so <c>/projects/webshop-api/environments/prod</c> is
/// the path. The id is in every answer, because a token binding is by id and so
/// is the refusal that names what a token could not reach.
/// <para>
/// The scopes are declared here and enforced by the one middleware: <c>names</c>
/// to see the catalogue, <c>write</c> to change it, <c>delete</c> to delete and
/// to restore — restoring is the undo of a deletion and travels with it (§5).
/// None of this is human-only: creating projects and environments is explicitly
/// something an agent may do (§6.4).
/// </para>
/// </remarks>
public static class ProjectEndpoints
{
    private const string Tag = "Projects";

    public static IEndpointRouteBuilder MapProjects(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup($"{ApiVersion.Route}/projects").WithTags(Tag);

        api.MapGet("", async (
                bool? deleted, ListProjects list, CancellationToken cancellation) =>
                (await list.ExecuteAsync(deleted ?? false, cancellation))
                    .Select(Catalogued.Project).ToList())
            .Needing(Scopes.Names)
            .WithName("ReadProjects")
            .WithSummary("Every project this token reaches. `deleted=true` lists the recoverable ones.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("", async (
                CreateProjectRequest request, CreateProject create, CancellationToken cancellation) =>
                Results.Ok(Catalogued.Project(
                    await create.ExecuteAsync(request.Name, request.Environments, cancellation))))
            .Needing(Scopes.Write)
            .WithName("CreateProject")
            .WithSummary("Create a project, with dev, staging and prod unless others are named.")
            .Produces<ProjectShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet("/{project}", async (
                string project, ReadProject read, CancellationToken cancellation) =>
                Catalogued.Project(await read.ExecuteAsync(project, cancellation)))
            .Needing(Scopes.Names)
            .WithName("ReadProject")
            .WithSummary("One project by name, with the environments this token reaches.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPatch("/{project}", async (
                string project,
                RenameRequest request,
                RenameProject rename,
                CancellationToken cancellation) =>
                Catalogued.Project(await rename.ExecuteAsync(project, request.Name, cancellation)))
            .Needing(Scopes.Write)
            .WithName("RenameProject")
            .WithSummary("Rename a project.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete("/{project}", async (
                string project, DeleteProject delete, CancellationToken cancellation) =>
                Catalogued.Project(await delete.ExecuteAsync(project, cancellation)))
            .Needing(Scopes.Delete)
            .WithName("DeleteProject")
            .WithSummary("Delete a project recoverably. Its environments are retained, not deleted.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/{project}/restore", async (
                string project, RestoreProject restore, CancellationToken cancellation) =>
                Results.Ok(Catalogued.Project(await restore.ExecuteAsync(project, cancellation))))
            .Needing(Scopes.Delete)
            .WithName("RestoreProject")
            .WithSummary("Bring a deleted project back inside its recovery window.")
            .Produces<ProjectShape>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone);

        api.MapPost("/{project}/purge", async (
                string project, PurgeProject purge, CancellationToken cancellation) =>
                Results.Ok(await purge.ExecuteAsync(project, cancellation)))
            .Needing(Scopes.Delete)
            .HumanOnly(HumanAction.Purge)
            .WithName("PurgeProject")
            .WithSummary("Remove a deleted project and its retained subtree, now. Human sessions only.")
            .Produces<Purged>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/{project}/environments/{environment}/purge", async (
                string project,
                string environment,
                PurgeEnvironment purge,
                CancellationToken cancellation) =>
                Results.Ok(await purge.ExecuteAsync(project, environment, cancellation)))
            .Needing(Scopes.Delete)
            .HumanOnly(HumanAction.Purge)
            .WithName("PurgeEnvironment")
            .WithSummary("Remove a deleted environment and its secrets, now. Human sessions only.")
            .Produces<Purged>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/{project}/environments", async (
                string project,
                bool? deleted,
                ListEnvironments list,
                CancellationToken cancellation) =>
                (await list.ExecuteAsync(project, deleted ?? false, cancellation))
                    .Select(Catalogued.Environment).ToList())
            .Needing(Scopes.Names)
            .WithName("ReadEnvironments")
            .WithSummary("Every environment of a project. `deleted=true` lists the recoverable ones.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/{project}/environments", async (
                string project,
                CreateEnvironmentRequest request,
                CreateEnvironment create,
                CancellationToken cancellation) =>
                Results.Ok(Catalogued.Environment(
                    await create.ExecuteAsync(project, request.Name, cancellation))))
            .Needing(Scopes.Write)
            .WithName("CreateEnvironment")
            .WithSummary("Add an environment to a project. Freely nameable, and no privacy.")
            .Produces<EnvironmentShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPatch("/{project}/environments/{environment}", async (
                string project,
                string environment,
                RenameRequest request,
                RenameEnvironment rename,
                CancellationToken cancellation) =>
                Catalogued.Environment(
                    await rename.ExecuteAsync(project, environment, request.Name, cancellation)))
            .Needing(Scopes.Write)
            .WithName("RenameEnvironment")
            .WithSummary("Rename an environment.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete("/{project}/environments/{environment}", async (
                string project,
                string environment,
                DeleteEnvironment delete,
                CancellationToken cancellation) =>
                Catalogued.Environment(
                    await delete.ExecuteAsync(project, environment, cancellation)))
            .Needing(Scopes.Delete)
            .WithName("DeleteEnvironment")
            .WithSummary("Delete an environment recoverably.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/{project}/environments/{environment}/restore", async (
                string project,
                string environment,
                RestoreEnvironment restore,
                CancellationToken cancellation) =>
                Results.Ok(Catalogued.Environment(
                    await restore.ExecuteAsync(project, environment, cancellation))))
            .Needing(Scopes.Delete)
            .WithName("RestoreEnvironment")
            .WithSummary("Bring a deleted environment back inside its recovery window.")
            .Produces<EnvironmentShape>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone);

        return endpoints;
    }
}

/// <summary>The catalogue's rows, as the contract shows them.</summary>
public static class Catalogued
{
    public static EnvironmentShape Environment(EnvironmentRow row) =>
        new(row.Id, row.ProjectId, row.Name, row.CreatedAt, row.DeletedAt);

    public static ProjectShape Project(ProjectRow row) =>
        new(
            row.Id,
            row.Name,
            row.CreatedAt,
            row.DeletedAt,
            [.. row.Environments.Select(Environment)]);
}
