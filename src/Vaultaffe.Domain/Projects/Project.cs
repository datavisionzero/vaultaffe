using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.References;

namespace Vaultaffe.Domain.Projects;

/// <summary>
/// An application or service — <c>webshop-api</c>, <c>landing-page</c>
/// (Specification §5). Project names are unique within an organization, and
/// there is deliberately no grouping level above one: the organization is the
/// grouping mechanism.
/// </summary>
public sealed class Project : IBelongToAnOrganization
{
    public Project(Guid id, Guid organizationId, string name, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = ReferenceName.Require(name, nameof(name));
        CreatedAt = createdAt;
    }

    /// <summary>
    /// The environments a project is created with (Specification §5: freely
    /// nameable, with sensible defaults). Nothing stops a project from having
    /// others, or none of these.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultEnvironments = ["dev", "staging", "prod"];

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When this project was deleted, or null. Deletion is recoverable
    /// (Specification §6.5): the row stays, out of every listing, until its
    /// window ends or a human purges it. The name stays reserved for exactly as
    /// long, which is why the unique index does not exclude deleted rows.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Whether this project is out of listings and use.</summary>
    public bool IsDeleted => DeletedAt is not null;

    /// <summary>
    /// Delete, recoverably. Repeating it does not extend the deadline
    /// (Specification §6.5), so a second call is deliberately a no-op.
    /// </summary>
    public void DeleteAt(DateTimeOffset moment) => DeletedAt ??= moment;

    /// <summary>Restore. The window is checked by the act, not by the record.</summary>
    public void Restore() => DeletedAt = null;

    public void RenameTo(string name) => Name = ReferenceName.Require(name, nameof(name));
}
