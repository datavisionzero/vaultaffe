using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.Secrets;

/// <summary>
/// A key/value pair within an environment (Specification §5). The value is
/// encrypted at rest under a data key of this secret's own, which is in turn
/// wrapped by the instance master key (§6.3) — so rotating that master key is
/// later a rewrap of data keys rather than a re-encryption of every value.
/// </summary>
/// <remarks>
/// What those bytes mean — which algorithm, how the data key is wrapped, how a
/// nonce is chosen — is deliberately not decided here. This type says only that
/// a value never rests unsealed and that a secret without one is a placeholder.
/// </remarks>
public sealed class Secret : IBelongToAnOrganization
{
    public Secret(
        Guid id,
        Guid organizationId,
        Guid environmentId,
        string name,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        Name = SecretName.Require(name, nameof(name));
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid EnvironmentId { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc cref="Projects.Project.DeletedAt"/>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>
    /// This secret's data key, wrapped by the instance master key. Null until
    /// the secret first holds a value; kept across every later write, because a
    /// data key that changed per value would make a master-key rotation a
    /// re-encryption again.
    /// </summary>
    public byte[]? WrappedDataKey { get; private set; }

    /// <summary>The nonce the current value was sealed with.</summary>
    public byte[]? Nonce { get; private set; }

    /// <summary>The current value, sealed. Null means an empty placeholder.</summary>
    public byte[]? Ciphertext { get; private set; }

    /// <summary>When the current value was last written.</summary>
    public DateTimeOffset? ValueWrittenAt { get; private set; }

    /// <summary>
    /// An empty placeholder: the key exists, a human still has to fill it
    /// (Specification §6.2). It is what makes <c>run</c> refuse to start and name
    /// the key, and what the names listing reports as status — so it is a state
    /// of the secret, never an empty string pretending to be a value.
    /// </summary>
    public bool IsPlaceholder => Ciphertext is null;

    /// <summary>
    /// Put a sealed value in place and hand back the one it replaced, or null if
    /// this secret was a placeholder. The caller decides whether it was allowed
    /// to overwrite (Specification §6.2) and what becomes of the returned
    /// version; this only refuses to lose one silently.
    /// </summary>
    public SecretValueVersion? Seal(Guid versionId, SealedValue value, DateTimeOffset moment)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (WrappedDataKey is not null && !WrappedDataKey.SequenceEqual(value.WrappedDataKey))
        {
            throw new InvalidOperationException(
                "A secret keeps one data key for its lifetime; replacing it would strand "
                + "every version sealed under the old one.");
        }

        var superseded = Ciphertext is null
            ? null
            : new SecretValueVersion(
                versionId, OrganizationId, Id, Nonce!, Ciphertext, ValueWrittenAt ?? CreatedAt, moment);

        WrappedDataKey = value.WrappedDataKey;
        Nonce = value.Nonce;
        Ciphertext = value.Ciphertext;
        ValueWrittenAt = moment;

        return superseded;
    }

    /// <inheritdoc cref="Projects.Project.DeleteAt"/>
    public void DeleteAt(DateTimeOffset moment) => DeletedAt ??= moment;

    public void Restore() => DeletedAt = null;
}
