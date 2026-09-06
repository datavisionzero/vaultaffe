using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.History;

/// <summary>
/// First and last use of one secret by one identity — the access summary
/// (Specification §6.5).
/// </summary>
/// <remarks>
/// <b>A summary, and deliberately not an execution history.</b> A successful read
/// does not prove that an application started, and nothing in this product
/// promises an event per application start; two moments per identity and secret
/// is what can be said honestly, so it is all that is stored. A row per read
/// would be the thing §6.5 refuses to pretend to, and it would grow without
/// bound in front of a <c>run</c> that happens every few seconds.
/// <para>
/// <b>No value, here as little as in the change log</b>, and no column that could
/// hold one.
/// </para>
/// <para>
/// The identity is recorded the way the change log records it — an id, its type,
/// and the name it went by — and is not a foreign key, so a revoked token's line
/// still reads as something other than a bare id.
/// </para>
/// <para>
/// Nothing here has a method that moves <see cref="LastAt"/>, and that is not an
/// oversight: the row is written by one statement in the store, because two
/// processes reading the same key at the same moment must not race each other
/// into a duplicate. The rule that statement carries is the one this type
/// declares by shape — <see cref="FirstAt"/> is written once and never again,
/// <see cref="LastAt"/> only ever moves forward.
/// </para>
/// </remarks>
public sealed class SecretAccess : IBelongToAnOrganization
{
    public SecretAccess(
        Guid id,
        Guid organizationId,
        Guid secretId,
        Guid identityId,
        IdentityType identityType,
        string identityName,
        DateTimeOffset firstAt,
        DateTimeOffset lastAt)
    {
        Id = id;
        OrganizationId = organizationId;
        SecretId = secretId;
        IdentityId = identityId;
        IdentityType = identityType;
        IdentityName = identityName;
        FirstAt = firstAt;
        LastAt = lastAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid SecretId { get; private set; }

    /// <summary>Who read it. Not a foreign key, for the reason in the remarks.</summary>
    public Guid IdentityId { get; private set; }

    public IdentityType IdentityType { get; private set; }

    /// <summary>What that identity is called — kept current, so a renamed token reads as it is now.</summary>
    public string IdentityName { get; private set; }

    /// <summary>The first time this identity read this secret. It never moves again.</summary>
    public DateTimeOffset FirstAt { get; private set; }

    /// <summary>The last time it did.</summary>
    public DateTimeOffset LastAt { get; private set; }
}
