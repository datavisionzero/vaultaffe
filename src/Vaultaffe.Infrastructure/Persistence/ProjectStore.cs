using Microsoft.EntityFrameworkCore;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Projects;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Infrastructure.Persistence;

/// <summary>
/// The project and environment rows, over the one context
/// (<c>docs/storage.md</c>).
/// </summary>
/// <remarks>
/// Nothing here calls <c>IgnoreQueryFilters</c>, and that is worth saying out
/// loud beside <see cref="IdentityStore"/>, where six queries do: everybody who
/// asks these questions was already put inside an organization by their token, so
/// the filter of Specification §9 applies to every line of this file.
/// <para>
/// A deleted row is still a row. The listings leave one out because a deleted
/// object is out of listings and use (§6.5); the lookups return one because
/// restoring needs it and because creating something of the same name has to be
/// refused by it.
/// </para>
/// </remarks>
public sealed class ProjectStore(VaultaffeDbContext context) : IProjectStore
{
    public async Task<IReadOnlyList<Project>> ListProjectsAsync(
        CancellationToken cancellationToken) =>
        await context.Projects
            .Where(project => project.DeletedAt == null)
            .OrderBy(project => project.Name)
            .ToListAsync(cancellationToken);

    public Task<Project?> FindProjectAsync(string name, CancellationToken cancellationToken) =>
        context.Projects.FirstOrDefaultAsync(project => project.Name == name, cancellationToken);

    public async Task AddProjectAsync(
        Project project,
        IReadOnlyList<Environment> environments,
        CancellationToken cancellationToken)
    {
        context.Add(project);
        context.AddRange(environments);

        // One SaveChanges: the project, its environments, and the change-log
        // entries the act recorded, in the same transaction. A project whose
        // default environments half arrived is not one anybody asked for.
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Environment>> ListEnvironmentsAsync(
        Guid projectId, CancellationToken cancellationToken) =>
        await Live(one => one.ProjectId == projectId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Environment>> ListEnvironmentsAsync(
        IReadOnlyList<Guid> projectIds, CancellationToken cancellationToken) =>
        projectIds.Count is 0
            ? []
            : await Live(one => projectIds.Contains(one.ProjectId)).ToListAsync(cancellationToken);

    public Task<Environment?> FindEnvironmentAsync(
        Guid projectId, string name, CancellationToken cancellationToken) =>
        context.Environments.FirstOrDefaultAsync(
            one => one.ProjectId == projectId && one.Name == name, cancellationToken);

    public async Task AddEnvironmentAsync(
        Environment environment, CancellationToken cancellationToken)
    {
        context.Add(environment);

        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    private IQueryable<Environment> Live(
        System.Linq.Expressions.Expression<Func<Environment, bool>> which) =>
        context.Environments
            .Where(one => one.DeletedAt == null)
            .Where(which)
            .OrderBy(one => one.Name);
}
