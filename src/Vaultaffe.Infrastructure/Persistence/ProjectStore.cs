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

    public async Task<IReadOnlyList<Project>> ListDeletedProjectsAsync(
        CancellationToken cancellationToken) =>
        await context.Projects
            .Where(project => project.DeletedAt != null)
            .OrderByDescending(project => project.DeletedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Environment>> ListDeletedEnvironmentsAsync(
        Guid projectId, CancellationToken cancellationToken) =>
        await context.Environments
            .Where(one => one.ProjectId == projectId && one.DeletedAt != null)
            .OrderByDescending(one => one.DeletedAt)
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

    /// <summary>
    /// The subtree goes explicitly. The schema has no cascade between the
    /// containers on purpose — one would have made every ordinary deletion
    /// permanent — so the one case where permanent is what was asked for spells
    /// it out. Tracked removal rather than ExecuteDelete, so that the change-log
    /// entry recorded about this commits in the same transaction.
    /// </summary>
    public async Task PurgeAsync(Project project, CancellationToken cancellationToken)
    {
        var environments = await context.Environments
            .Where(one => one.ProjectId == project.Id)
            .ToListAsync(cancellationToken);

        foreach (var environment in environments)
        {
            await RemoveSecretsOfAsync(environment.Id, cancellationToken);
        }

        context.RemoveRange(environments);
        context.Remove(project);
    }

    public async Task PurgeAsync(Environment environment, CancellationToken cancellationToken)
    {
        await RemoveSecretsOfAsync(environment.Id, cancellationToken);

        context.Remove(environment);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    // A secret's retained values follow it, which is the one cascade this schema
    // declares and exactly the case it was declared for.
    private async Task RemoveSecretsOfAsync(Guid environmentId, CancellationToken cancellationToken) =>
        context.RemoveRange(
            await context.Secrets
                .Where(secret => secret.EnvironmentId == environmentId)
                .ToListAsync(cancellationToken));

    private IQueryable<Environment> Live(
        System.Linq.Expressions.Expression<Func<Environment, bool>> which) =>
        context.Environments
            .Where(one => one.DeletedAt == null)
            .Where(which)
            .OrderBy(one => one.Name);
}
