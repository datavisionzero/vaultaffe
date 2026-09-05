using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.Tokens;

/// <summary>
/// One project, or one environment of one project, that a token may touch. A
/// token with no bindings at all reaches its whole organization; excluding the
/// production environment is the first switch offered when a human creates an
/// agent token (Specification §6.4).
/// </summary>
public sealed class TokenBinding : IBelongToAnOrganization
{
    public TokenBinding(
        Guid id,
        Guid organizationId,
        Guid tokenId,
        Guid projectId,
        Guid? environmentId)
    {
        Id = id;
        OrganizationId = organizationId;
        TokenId = tokenId;
        ProjectId = projectId;
        EnvironmentId = environmentId;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid TokenId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>One environment of the project, or null for all of them.</summary>
    public Guid? EnvironmentId { get; private set; }
}
