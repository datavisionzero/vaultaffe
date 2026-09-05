using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.References;

namespace Vaultaffe.Domain.Environments;

/// <summary>
/// <c>dev</c>, <c>staging</c>, <c>prod</c> within a project (Specification §5).
/// Freely nameable, unique within its project, and one concept rather than
/// Doppler's environment plus config: there is no branch config, no personal
/// config and therefore not a single exception to "everyone in the organization
/// sees everything".
/// </summary>
/// <remarks>
/// The name collides with <see cref="System.Environment"/>. That is accepted
/// rather than worked around: this is the word the product, the CLI and the
/// specification use, and a domain type named <c>EnvironmentEntity</c> would be
/// a naming bug we chose on purpose. A file that needs both qualifies one.
/// </remarks>
public sealed class Environment : IBelongToAnOrganization
{
    public Environment(
        Guid id,
        Guid organizationId,
        Guid projectId,
        string name,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ProjectId = projectId;
        Name = ReferenceName.Require(name, nameof(name));
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc cref="Projects.Project.DeletedAt"/>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public void DeleteAt(DateTimeOffset moment) => DeletedAt ??= moment;

    public void Restore() => DeletedAt = null;

    public void RenameTo(string name) => Name = ReferenceName.Require(name, nameof(name));
}
