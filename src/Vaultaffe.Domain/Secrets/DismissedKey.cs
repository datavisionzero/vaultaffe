using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.Secrets;

/// <summary>
/// A key the missing-key notice was told not to mention in this environment
/// again (Specification §6.1).
/// </summary>
/// <remarks>
/// One row per environment and key, because that is the granularity of the
/// answer: "this environment does not need that key" is a statement about one
/// place, and the same key may well be missing somewhere it is genuinely wanted.
/// <para>
/// It carries no value, no author and no reason. It is not a change to a secret
/// and it does not appear in the change log (§6.5) — nothing about the vault is
/// different afterwards, only what a screen says. A dismissal is withdrawn by
/// deleting the row, which is why there is no <c>deleted_at</c> here: the one
/// thing a dismissal is recoverable from is itself.
/// </para>
/// </remarks>
public sealed class DismissedKey : IBelongToAnOrganization
{
    public DismissedKey(
        Guid id,
        Guid organizationId,
        Guid environmentId,
        string name,
        DateTimeOffset dismissedAt)
    {
        Id = id;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        Name = SecretName.Require(name, nameof(name));
        DismissedAt = dismissedAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid EnvironmentId { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset DismissedAt { get; private set; }
}
