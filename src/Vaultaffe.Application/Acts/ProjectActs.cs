using Vaultaffe.Application.Authorization;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Projects;
using Vaultaffe.Domain.References;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Secrets;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Application.Acts;

/// <summary>An environment as every listing shows it.</summary>
public sealed record EnvironmentRow(
    Guid Id,
    Guid ProjectId,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

/// <summary>A project, and the environments the caller may see of it.</summary>
public sealed record ProjectRow(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<EnvironmentRow> Environments);

/// <summary>
/// What every act on a project or an environment does before it does anything
/// else: find the thing by the name it was asked about, and refuse the four ways
/// that can fail.
/// </summary>
/// <remarks>
/// Names and not ids, because a name is what a person types, what a
/// <c>vaultaffe://</c> reference carries and what the CLI's directory binding
/// holds (Specification §5). The id exists and is in every answer — a token
/// binding is by id, and so is a refusal that names what a token could not reach.
/// </remarks>
internal static class Catalogue
{
    /// <summary>A name off the wire, or the refusal that says what a name is.</summary>
    public static string ANameFrom(string? name, string field)
    {
        var trimmed = (name ?? string.Empty).Trim();

        return ReferenceName.IsValid(trimmed)
            ? trimmed
            : throw Refusal.Validation(field, ReferenceName.Explanation);
    }

    /// <summary>
    /// The project of that name, in use. A deleted one is not found: it is out of
    /// listings and use for as long as it is recoverable (§6.5), and only
    /// <see cref="Deleted"/> looks at one.
    /// </summary>
    public static async Task<Project> InUseAsync(
        IProjectStore projects, string name, CancellationToken cancellationToken)
    {
        var found = await projects.FindProjectAsync(
            ANameFrom(name, "project"), cancellationToken);

        return found is null || found.IsDeleted
            ? throw Refusal.NotFound($"No project called '{name}'.")
            : found;
    }

    /// <summary>
    /// The environment of that name in that project, in use. A deleted project's
    /// environments are not in use either, which is why the project is found
    /// first.
    /// </summary>
    public static async Task<Environment> InUseAsync(
        IProjectStore projects, Project project, string name, CancellationToken cancellationToken)
    {
        var found = await projects.FindEnvironmentAsync(
            project.Id, ANameFrom(name, "environment"), cancellationToken);

        return found is null || found.IsDeleted
            ? throw Refusal.NotFound($"No environment called '{name}' in '{project.Name}'.")
            : found;
    }

    /// <summary>
    /// Whether a name is free, and the refusal when it is not. A deleted object
    /// holds its name for exactly as long as it can be restored, which is the
    /// answer to "I deleted it, why can I not recreate it" — so the refusal says
    /// which of the two cases it is rather than leaving somebody to guess.
    /// </summary>
    public static void MustBeFree(string what, string name, bool exists, bool deleted)
    {
        if (!exists)
        {
            return;
        }

        throw Refusal.NameTaken(
            deleted
                ? $"A deleted {what} is still called '{name}', and keeps that name for as long as "
                    + "it can be restored. Restore it, or purge it, or use another name."
                : $"There is already a {what} called '{name}'.",
            deleted);
    }

    /// <summary>
    /// Whether something deleted is still within its recovery window, and the
    /// refusal when it is not. The row outlives the window until a sweep or a
    /// purge removes it, so "still here" and "still recoverable" are two
    /// questions (§6.5).
    /// </summary>
    public static void MustStillBeRecoverable(
        string what, string name, DateTimeOffset deletedAt, DateTimeOffset now)
    {
        if (!ValueHistory.IsStillRecoverable(deletedAt, now))
        {
            throw Refusal.NotRecoverable(
                $"That {what} was deleted more than {ValueHistory.RecoveryWindow.TotalHours:0} "
                + $"hours ago, and '{name}' is no longer recoverable.");
        }
    }

    public static EnvironmentRow Row(Environment environment) =>
        new(
            environment.Id,
            environment.ProjectId,
            environment.Name,
            environment.CreatedAt,
            environment.DeletedAt);

    public static ProjectRow Row(Project project, IEnumerable<Environment> environments) =>
        new(
            project.Id,
            project.Name,
            project.CreatedAt,
            project.DeletedAt,
            [.. environments.Where(e => e.ProjectId == project.Id).Select(Row)]);
}

/// <summary>
/// Creating a project, with the environments it starts life with
/// (Specification §5).
/// </summary>
/// <remarks>
/// The defaults are <c>dev</c>, <c>staging</c> and <c>prod</c> because a project
/// with none is a project nothing can be written into yet, and three names cover
/// what most people would have typed. They are a default and not a rule: a
/// caller may name its own, and an environment called <c>dev-someone</c> is an
/// ordinary environment offering <b>no</b> privacy — everybody in the
/// organization sees it (§6.4).
/// <para>
/// Creating a project needs a token that reaches the whole organization. A bound
/// token is narrowed to projects that exist; a new one is by definition not among
/// them.
/// </para>
/// </remarks>
public sealed class CreateProject(
    IProjectStore projects, ChangeLog log, Authority authority, TimeProvider clock)
{
    public async Task<ProjectRow> ExecuteAsync(
        string name,
        IReadOnlyList<string>? environments,
        CancellationToken cancellationToken)
    {
        var acting = authority.RequiresTheWholeOrganization();
        var named = Catalogue.ANameFrom(name, "name");

        var existing = await projects.FindProjectAsync(named, cancellationToken);

        Catalogue.MustBeFree("project", named, existing is not null, existing?.IsDeleted ?? false);

        var wanted = environments ?? Project.DefaultEnvironments;
        var names = wanted.Select(one => Catalogue.ANameFrom(one, "environments")).ToList();

        if (names.Count != names.Distinct(StringComparer.Ordinal).Count())
        {
            throw Refusal.Validation(
                "environments", "An environment name appears twice; they are unique per project.");
        }

        var now = clock.GetUtcNow();
        var project = new Project(Guid.NewGuid(), acting.OrganizationId, named, now);

        var created = names
            .Select(one => new Environment(
                Guid.NewGuid(), acting.OrganizationId, project.Id, one, now))
            .ToList();

        log.Record(ChangeAction.Created, project: project.Name);

        foreach (var environment in created)
        {
            log.Record(ChangeAction.Created, project.Name, environment.Name);
        }

        await projects.AddProjectAsync(project, created, cancellationToken);

        return Catalogue.Row(project, created);
    }
}

/// <summary>
/// Every project the caller may see, with its environments.
/// </summary>
/// <remarks>
/// A binding narrows a listing rather than refusing it: a token bound to one
/// project sees that project, and being told "forbidden" for the eleven it is not
/// bound to would be a listing nobody could use. Deleted projects are out of it
/// entirely — they are out of listings and use until they are restored (§6.5).
/// </remarks>
public sealed class ListProjects(IProjectStore projects, Authority authority)
{
    public async Task<IReadOnlyList<ProjectRow>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var acting = authority.Caller;

        var visible = (await projects.ListProjectsAsync(cancellationToken))
            .Where(project => acting.Reaches(project.Id))
            .ToList();

        var environments = await projects.ListEnvironmentsAsync(
            [.. visible.Select(project => project.Id)], cancellationToken);

        return
        [
            .. visible.Select(project => Catalogue.Row(
                project,
                environments.Where(one => acting.Reaches(project.Id, one.Id)))),
        ];
    }
}

/// <summary>One project by name, with the environments the caller may see of it.</summary>
public sealed class ReadProject(IProjectStore projects, Authority authority)
{
    public async Task<ProjectRow> ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        var project = await Catalogue.InUseAsync(projects, name, cancellationToken);
        var acting = authority.RequiresReachInto(project.Id);

        var environments = await projects.ListEnvironmentsAsync(project.Id, cancellationToken);

        return Catalogue.Row(
            project, environments.Where(one => acting.Reaches(project.Id, one.Id)));
    }
}

/// <summary>
/// Renaming one. The change log records the new name, because that is the name
/// everything after this entry is about; the old one is in the entry before it.
/// </summary>
public sealed class RenameProject(IProjectStore projects, ChangeLog log, Authority authority)
{
    public async Task<ProjectRow> ExecuteAsync(
        string name, string newName, CancellationToken cancellationToken)
    {
        var project = await Catalogue.InUseAsync(projects, name, cancellationToken);

        authority.RequiresAllOf(project.Id);

        var renamed = Catalogue.ANameFrom(newName, "name");

        if (renamed != project.Name)
        {
            var existing = await projects.FindProjectAsync(renamed, cancellationToken);

            Catalogue.MustBeFree(
                "project", renamed, existing is not null, existing?.IsDeleted ?? false);

            project.RenameTo(renamed);
            log.Record(ChangeAction.Renamed, project: project.Name);

            await projects.SaveAsync(cancellationToken);
        }

        var environments = await projects.ListEnvironmentsAsync(project.Id, cancellationToken);

        return Catalogue.Row(project, environments);
    }
}

/// <summary>
/// Deleting one, recoverably (Specification §6.5).
/// </summary>
/// <remarks>
/// The environments underneath are not touched, and that is the promise rather
/// than an omission: the subtree is retained and restored <b>as one</b>, so
/// restoring the project brings back exactly the environments that were in use
/// when it went — including the fact that one of them had been deleted first.
/// Marking them all would lose that, and a database cascade would delete them
/// outright (<c>docs/storage.md</c>).
/// <para>
/// Repeating a deletion does not extend the window, which the record enforces:
/// a second call is deliberately uneventful and writes no second entry.
/// </para>
/// </remarks>
public sealed class DeleteProject(
    IProjectStore projects, ChangeLog log, Authority authority, TimeProvider clock)
{
    public async Task<ProjectRow> ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        var project = await Catalogue.InUseAsync(projects, name, cancellationToken);

        authority.RequiresAllOf(project.Id);

        project.DeleteAt(clock.GetUtcNow());
        log.Record(ChangeAction.Deleted, project: project.Name);

        await projects.SaveAsync(cancellationToken);

        var environments = await projects.ListEnvironmentsAsync(project.Id, cancellationToken);

        return Catalogue.Row(project, environments);
    }
}

/// <summary>
/// Bringing one back inside its window. An agent may do this: restoring is the
/// undo, and the scope that covers deleting covers it (§5).
/// </summary>
public sealed class RestoreProject(
    IProjectStore projects, ChangeLog log, Authority authority, TimeProvider clock)
{
    public async Task<ProjectRow> ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        var project = await projects.FindProjectAsync(
                Catalogue.ANameFrom(name, "project"), cancellationToken)
            ?? throw Refusal.NotFound($"No project called '{name}'.");

        authority.RequiresAllOf(project.Id);

        // Restoring something that is not deleted is what the caller wanted to be
        // true and already is. It is not an error, and it is not a second entry
        // in the log either.
        if (project.DeletedAt is { } deletedAt)
        {
            Catalogue.MustStillBeRecoverable(
                "project", project.Name, deletedAt, clock.GetUtcNow());

            project.Restore();
            log.Record(ChangeAction.Restored, project: project.Name);

            await projects.SaveAsync(cancellationToken);
        }

        var environments = await projects.ListEnvironmentsAsync(project.Id, cancellationToken);

        return Catalogue.Row(project, environments);
    }
}
