using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.History;

/// <summary>
/// One recorded mutation: when, where, what, and by whom — with the identity's
/// type beside it (Specification §6.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no value on this type, and there is no column for one.</b> Not the
/// new value, not the old one, not a diff. That is the difference between AWS and
/// Doppler, and Doppler is the one that had to bolt on a redaction feature which
/// does not truly delete anything. A field that could hold a value is the bug.
/// </para>
/// <para>
/// The subject is recorded by <b>name</b> rather than by foreign key, because
/// neither a purge nor an expiry removes change-log entries: the log outlives the
/// project, environment and secret it talks about, and a key pointing at a row
/// that is gone would take the entry with it.
/// </para>
/// </remarks>
public sealed class ChangeLogEntry : IBelongToAnOrganization
{
    public ChangeLogEntry(
        Guid id,
        Guid organizationId,
        DateTimeOffset occurredAt,
        Guid identityId,
        IdentityType identityType,
        string identityName,
        ChangeAction action,
        string? projectName = null,
        string? environmentName = null,
        string? secretName = null)
    {
        Id = id;
        OrganizationId = organizationId;
        OccurredAt = occurredAt;
        IdentityId = identityId;
        IdentityType = identityType;
        IdentityName = identityName;
        Action = action;
        ProjectName = projectName;
        EnvironmentName = environmentName;
        SecretName = secretName;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Who acted. Not a foreign key, for the reason in the type's remarks.</summary>
    public Guid IdentityId { get; private set; }

    public IdentityType IdentityType { get; private set; }

    /// <summary>
    /// What that identity was called at the time — the person's name, or the name
    /// a human gave the token. Kept here so a revoked token's entries still read
    /// as something other than a bare id.
    /// </summary>
    public string IdentityName { get; private set; }

    public ChangeAction Action { get; private set; }

    public string? ProjectName { get; private set; }

    public string? EnvironmentName { get; private set; }

    public string? SecretName { get; private set; }
}
