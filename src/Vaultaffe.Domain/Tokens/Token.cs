using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.Tokens;

/// <summary>
/// An access key for the CLI and for automation (Specification §5): a kind, a
/// binding, a scope set, and the hash of a value that was shown exactly once.
/// </summary>
/// <remarks>
/// What a token value looks like — the prefix per kind that lets a secret scanner
/// find it in a repository or a log, and how it is hashed — is
/// <see cref="TokenValue"/>. This type is the credential's own record: whose it
/// is, what it may touch, what it may do, and whether it still counts.
/// </remarks>
public sealed class Token : IBelongToAnOrganization
{
    private readonly List<TokenBinding> _bindings = [];

    public Token(
        Guid id,
        Guid organizationId,
        Guid userId,
        TokenKind kind,
        string? name,
        byte[] valueHash,
        Scopes scopes,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(valueHash);

        Id = id;
        OrganizationId = organizationId;
        UserId = userId;
        Kind = kind;
        Name = name;
        ValueHash = valueHash;
        Scopes = scopes;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Mint one: a value of that kind, and the record that keeps nothing but its
    /// hash. Two returns rather than one, because the value exists exactly once —
    /// the caller shows it and lets it go, and nothing can ask this record for it
    /// afterwards (Specification §6.1).
    /// </summary>
    public static (Token Token, TokenValue Value) Issue(
        Guid id,
        Guid organizationId,
        Guid userId,
        TokenKind kind,
        string? name,
        Scopes scopes,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt = null)
    {
        var value = TokenValue.Issue(kind);

        return (
            new Token(
                id, organizationId, userId, kind, name, value.Hash(), scopes, createdAt, expiresAt),
            value);
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// Whose token this is. For a session token that is the person it
    /// authenticates; for a service or an agent token it is the person who
    /// created it and is accountable for what it does — a human creates an agent
    /// token and hands it over (Specification §6.4), and the trail from a machine
    /// back to a person is exactly what the change log's identity type is for
    /// (§6.5).
    /// </summary>
    public Guid UserId { get; private set; }

    public TokenKind Kind { get; private set; }

    /// <summary>What a human called it, so a revocation list is readable. Session tokens have none.</summary>
    public string? Name { get; private set; }

    /// <summary>
    /// The hash of the value. The value itself is shown once at creation and
    /// never stored — a stored token would be a credential this product holds in
    /// the clear, which is the opposite of what it is for.
    /// </summary>
    public byte[] ValueHash { get; private set; }

    public Scopes Scopes { get; private set; }

    /// <summary>
    /// Which projects and environments this token may touch. Empty means the
    /// whole organization, which is what an agent token gets unless the human
    /// narrows it: the point is attribution, not restriction (§6.4).
    /// </summary>
    public IReadOnlyCollection<TokenBinding> Bindings => _bindings;

    /// <summary>Whether this token is bound to the whole organization.</summary>
    public bool ReachesTheWholeOrganization => _bindings.Count == 0;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>
    /// When it was revoked, or null. Revoked rather than deleted, so that
    /// everything it ever signed in the change log keeps an author.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Whether this token still authenticates anything at <paramref name="moment"/>.</summary>
    public bool IsUsableAt(DateTimeOffset moment) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > moment);

    /// <summary>Whether this token carries every scope in <paramref name="wanted"/>.</summary>
    public bool Allows(Scopes wanted) => (Scopes & wanted) == wanted;

    /// <summary>Narrow this token to a project, or to one environment of it.</summary>
    public void BindTo(Guid bindingId, Guid projectId, Guid? environmentId = null) =>
        _bindings.Add(new TokenBinding(bindingId, OrganizationId, Id, projectId, environmentId));

    /// <summary>Revoke. Repeating it does not move the moment it happened.</summary>
    public void RevokeAt(DateTimeOffset moment) => RevokedAt ??= moment;
}
