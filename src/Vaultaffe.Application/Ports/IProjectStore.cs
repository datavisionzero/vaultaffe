using Vaultaffe.Domain.Projects;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Application.Ports;

/// <summary>
/// The rows projects and environments are kept in.
/// </summary>
/// <remarks>
/// One port for both levels, because nothing here reads one without the other: a
/// project is created with its environments in one act, and an environment is
/// never found except inside a project that was found first.
/// <para>
/// <b>Nothing here reaches past the organization filter.</b> Every question this
/// port answers is asked by somebody a token already put inside an organization
/// (Specification §9), which is the difference between this port and
/// <see cref="IIdentityStore"/>.
/// </para>
/// <para>
/// A deleted row is still a row (§6.5), so every lookup here finds one and the
/// act decides what that means: a listing leaves it out, a restore needs it, and
/// creating something of the same name is refused by it. A store that hid deleted
/// rows would make "the name stays reserved" a thing nobody could explain.
/// </para>
/// </remarks>
public interface IProjectStore
{
    /// <summary>Every project of this organization, deleted ones left out, by name.</summary>
    Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken);

    /// <summary>The project of that name, deleted or not, or null.</summary>
    Task<Project?> FindProjectAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// A project and the environments it is created with, in one transaction. A
    /// project whose default environments half arrived is not one anybody asked
    /// for.
    /// </summary>
    Task AddProjectAsync(
        Project project, IReadOnlyList<Environment> environments, CancellationToken cancellationToken);

    /// <summary>Every environment of that project, deleted ones left out, by name.</summary>
    Task<IReadOnlyList<Environment>> ListEnvironmentsAsync(
        Guid projectId, CancellationToken cancellationToken);

    /// <summary>The environments of several projects at once, so a listing is two queries.</summary>
    Task<IReadOnlyList<Environment>> ListEnvironmentsAsync(
        IReadOnlyList<Guid> projectIds, CancellationToken cancellationToken);

    /// <summary>That environment of that project, deleted or not, or null.</summary>
    Task<Environment?> FindEnvironmentAsync(
        Guid projectId, string name, CancellationToken cancellationToken);

    Task AddEnvironmentAsync(Environment environment, CancellationToken cancellationToken);

    /// <summary>
    /// Write back what the acts changed — and, in the same transaction, whatever
    /// the change log recorded about it.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
