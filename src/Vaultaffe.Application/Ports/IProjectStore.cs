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

    /// <summary>
    /// The deleted projects of this organization, newest deletion first — what a
    /// person picks from to restore or to purge. Purge is a visible feature and
    /// not a support ticket (Specification §6.5), and a feature nothing can list
    /// is one.
    /// </summary>
    Task<IReadOnlyList<Project>> ListDeletedProjectsAsync(CancellationToken cancellationToken);

    /// <summary>The deleted environments of that project, newest deletion first.</summary>
    Task<IReadOnlyList<Environment>> ListDeletedEnvironmentsAsync(
        Guid projectId, CancellationToken cancellationToken);

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
    /// Mark a project and everything retained under it for permanent removal
    /// (§6.5). Enlists rather than writes, so the entry the change log recorded
    /// about it commits with it.
    /// </summary>
    /// <remarks>
    /// The subtree goes explicitly, because the schema deliberately has no
    /// cascade between the containers: a database cascade would have made every
    /// ordinary deletion permanent, and this is the one place where permanent is
    /// what was asked for.
    /// </remarks>
    Task PurgeAsync(Project project, CancellationToken cancellationToken);

    /// <summary>The same, one level down: an environment and the secrets in it.</summary>
    Task PurgeAsync(Environment environment, CancellationToken cancellationToken);

    /// <summary>
    /// Write back what the acts changed — and, in the same transaction, whatever
    /// the change log recorded about it.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
