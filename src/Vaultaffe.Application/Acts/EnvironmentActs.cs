using Vaultaffe.Application.Authorization;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Refusals;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Application.Acts;

/// <summary>
/// Adding an environment to a project (Specification §5).
/// </summary>
/// <remarks>
/// Freely nameable, and one level: there is no branch config and no personal
/// config below it. An extra <c>dev-someone</c> is an ordinary environment and
/// offers <b>no</b> privacy — everybody in the organization sees it, and personal
/// credentials are outside the MVP (§6.4, §7).
/// <para>
/// It needs the project as a whole. A token bound to one environment of a project
/// must not be able to create the one beside it, or the binding would be a
/// suggestion.
/// </para>
/// </remarks>
public sealed class CreateEnvironment(
    IProjectStore projects, ChangeLog log, Authority authority, TimeProvider clock)
{
    public async Task<EnvironmentRow> ExecuteAsync(
        string project, string name, CancellationToken cancellationToken)
    {
        var found = await Catalogue.InUseAsync(projects, project, cancellationToken);
        var acting = authority.RequiresAllOf(found.Id);

        var named = Catalogue.ANameFrom(name, "name");
        var existing = await projects.FindEnvironmentAsync(found.Id, named, cancellationToken);

        Catalogue.MustBeFree(
            "environment", named, existing is not null, existing?.IsDeleted ?? false);

        var environment = new Environment(
            Guid.NewGuid(), acting.OrganizationId, found.Id, named, clock.GetUtcNow());

        log.Record(ChangeAction.Created, found.Name, environment.Name);

        await projects.AddEnvironmentAsync(environment, cancellationToken);

        return Catalogue.Row(environment);
    }
}

/// <summary>
/// Every environment of a project the caller may see. A binding narrows the
/// listing rather than refusing it, exactly as it does one level up.
/// </summary>
public sealed class ListEnvironments(IProjectStore projects, Authority authority)
{
    public async Task<IReadOnlyList<EnvironmentRow>> ExecuteAsync(
        string project, CancellationToken cancellationToken)
    {
        var found = await Catalogue.InUseAsync(projects, project, cancellationToken);
        var acting = authority.RequiresReachInto(found.Id);

        return
        [
            .. (await projects.ListEnvironmentsAsync(found.Id, cancellationToken))
                .Where(one => acting.Reaches(found.Id, one.Id))
                .Select(Catalogue.Row),
        ];
    }
}

/// <summary>Renaming one.</summary>
public sealed class RenameEnvironment(IProjectStore projects, ChangeLog log, Authority authority)
{
    public async Task<EnvironmentRow> ExecuteAsync(
        string project, string name, string newName, CancellationToken cancellationToken)
    {
        var found = await Catalogue.InUseAsync(projects, project, cancellationToken);
        var environment = await Catalogue.InUseAsync(projects, found, name, cancellationToken);

        authority.RequiresReachInto(found.Id, environment.Id);

        var renamed = Catalogue.ANameFrom(newName, "name");

        if (renamed != environment.Name)
        {
            var existing = await projects.FindEnvironmentAsync(
                found.Id, renamed, cancellationToken);

            Catalogue.MustBeFree(
                "environment", renamed, existing is not null, existing?.IsDeleted ?? false);

            environment.RenameTo(renamed);
            log.Record(ChangeAction.Renamed, found.Name, environment.Name);

            await projects.SaveAsync(cancellationToken);
        }

        return Catalogue.Row(environment);
    }
}

/// <summary>
/// Deleting one, recoverably. What hangs underneath is retained rather than
/// deleted with it, for the reason in <see cref="DeleteProject"/>.
/// </summary>
public sealed class DeleteEnvironment(
    IProjectStore projects, ChangeLog log, Authority authority, TimeProvider clock)
{
    public async Task<EnvironmentRow> ExecuteAsync(
        string project, string name, CancellationToken cancellationToken)
    {
        var found = await Catalogue.InUseAsync(projects, project, cancellationToken);
        var environment = await Catalogue.InUseAsync(projects, found, name, cancellationToken);

        authority.RequiresReachInto(found.Id, environment.Id);

        environment.DeleteAt(clock.GetUtcNow());
        log.Record(ChangeAction.Deleted, found.Name, environment.Name);

        await projects.SaveAsync(cancellationToken);

        return Catalogue.Row(environment);
    }
}

/// <summary>Bringing one back inside its window.</summary>
public sealed class RestoreEnvironment(
    IProjectStore projects, ChangeLog log, Authority authority, TimeProvider clock)
{
    public async Task<EnvironmentRow> ExecuteAsync(
        string project, string name, CancellationToken cancellationToken)
    {
        var found = await Catalogue.InUseAsync(projects, project, cancellationToken);

        var environment = await projects.FindEnvironmentAsync(
                found.Id, Catalogue.ANameFrom(name, "environment"), cancellationToken)
            ?? throw Refusal.NotFound($"No environment called '{name}' in '{found.Name}'.");

        authority.RequiresReachInto(found.Id, environment.Id);

        if (environment.DeletedAt is { } deletedAt)
        {
            Catalogue.MustStillBeRecoverable(
                "environment", environment.Name, deletedAt, clock.GetUtcNow());

            environment.Restore();
            log.Record(ChangeAction.Restored, found.Name, environment.Name);

            await projects.SaveAsync(cancellationToken);
        }

        return Catalogue.Row(environment);
    }
}
