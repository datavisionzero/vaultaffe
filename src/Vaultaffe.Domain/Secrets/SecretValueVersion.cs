using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.Secrets;

/// <summary>
/// A value this secret used to hold, kept so that a mistake has an undo button —
/// an agent that wrecks a value overnight is the case Specification §6.5 names.
/// It is sealed under the same data key as the secret's current value, and it
/// falls out at <see cref="ValueHistory.Versions"/> versions or
/// <see cref="ValueHistory.Window"/>, whichever is reached first, and is then
/// deleted rather than kept as a tombstone.
/// </summary>
public sealed class SecretValueVersion : IBelongToAnOrganization
{
    public SecretValueVersion(
        Guid id,
        Guid organizationId,
        Guid secretId,
        byte[] nonce,
        byte[] ciphertext,
        DateTimeOffset writtenAt,
        DateTimeOffset replacedAt)
    {
        Id = id;
        OrganizationId = organizationId;
        SecretId = secretId;
        Nonce = nonce;
        Ciphertext = ciphertext;
        WrittenAt = writtenAt;
        ReplacedAt = replacedAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid SecretId { get; private set; }

    public byte[] Nonce { get; private set; }

    /// <summary>
    /// Sealed under the secret's data key — the same one its current value uses,
    /// which is what keeps a master-key rotation a rewrap.
    /// </summary>
    public byte[] Ciphertext { get; private set; }

    /// <summary>When this value became the secret's current one.</summary>
    public DateTimeOffset WrittenAt { get; private set; }

    /// <summary>
    /// When it stopped being current. This is the clock the retention window
    /// runs on, not <see cref="WrittenAt"/>: a value that stood for a year and
    /// was replaced this morning is this morning's undo.
    /// </summary>
    public DateTimeOffset ReplacedAt { get; private set; }

    /// <summary>When this version falls out of the window on time alone.</summary>
    public DateTimeOffset ExpiresAt => ReplacedAt + ValueHistory.Window;
}
