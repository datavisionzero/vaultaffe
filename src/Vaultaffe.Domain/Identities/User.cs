using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.Identities;

/// <summary>
/// A person in an organization (Specification §6.1): an address they sign in
/// with, a password hash, and whether they administer the organization.
/// </summary>
/// <remarks>
/// Permissions for humans are deliberately trivial (§6.4): every user of an
/// organization sees and changes everything in it, and the only distinction is
/// who may invite users and administer the organization. That is the whole of
/// <see cref="IsAdministrator"/>, and there is no role model behind it.
/// <para>
/// A user is never the thing that authenticates a request — a token is. What
/// connects the two is <see cref="Tokens.Token.UserId"/>: a session token names
/// the person it belongs to, and a service or agent token names the person who
/// is accountable for it.
/// </para>
/// </remarks>
public sealed class User : IBelongToAnOrganization
{
    /// <summary>The longest a display name may be.</summary>
    public const int NameLimit = 100;

    public User(
        Guid id,
        Guid organizationId,
        string email,
        string name,
        string passwordHash,
        bool isAdministrator,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Email = EmailAddress.Require(email, nameof(email));
        Name = Named(name);
        PasswordHash = Hashed(passwordHash, nameof(passwordHash));
        IsAdministrator = isAdministrator;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// Normalized and lower-case, which is the spelling the unique index holds
    /// and the spelling a sign-in looks up by.
    /// </summary>
    public string Email { get; private set; }

    /// <summary>What the change log calls them, so an entry reads as a person.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// The password, hashed. Self-describing: the encoded value carries the
    /// algorithm and its parameters, so raising the cost later is a new hash on
    /// the next sign-in rather than a migration.
    /// </summary>
    public string PasswordHash { get; private set; }

    /// <summary>
    /// Whether this user may invite users and administer the organization
    /// (§6.4). The first user of an instance is one; everything else about
    /// permissions is the token's business.
    /// </summary>
    public bool IsAdministrator { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When an administrator took this person out of the organization, or null
    /// while they are in it.
    /// </summary>
    /// <remarks>
    /// Deactivated rather than deleted, for the reason a token is revoked rather
    /// than deleted: everything they ever changed keeps an author (§6.5). What it
    /// costs them is everything at once — <b>no token of theirs authenticates any
    /// more</b>, their sessions included and the agent tokens they are
    /// accountable for with them, because a person who is out of the organization
    /// cannot go on acting in it through something they left running.
    /// </remarks>
    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary>Whether this person is still in the organization.</summary>
    public bool IsActive => DeactivatedAt is null;

    /// <summary>
    /// Set a new password hash. An administrator performs a reset, because the
    /// instance sends no mail and therefore has no link to send (§6.1).
    /// </summary>
    public void ResetPasswordTo(string passwordHash) =>
        PasswordHash = Hashed(passwordHash, nameof(passwordHash));

    /// <summary>Rename.</summary>
    public void RenameTo(string name) => Name = Named(name);

    /// <summary>
    /// Change the address this person signs in with. An administrator's act for
    /// the same reason a reset is (§6.4): the address is the login name, and a
    /// mistyped one cannot correct itself, because the correction would need a
    /// sign-in that no longer works.
    /// </summary>
    /// <remarks>
    /// It is only this column. The password hash carries its own salt and does
    /// not depend on the address, and every token names a person by id — so
    /// nothing that was signed in stops working, and nothing in the history
    /// loses its author.
    /// </remarks>
    public void ChangeEmailTo(string email) => Email = EmailAddress.Require(email, nameof(email));

    /// <summary>Take them out of the organization. Repeating it does not move the moment.</summary>
    public void DeactivateAt(DateTimeOffset moment) => DeactivatedAt ??= moment;

    /// <summary>
    /// Put them back. Deactivation is the reversible half of this pair on
    /// purpose: it is a decision about a person and the second-most likely thing
    /// after making it is having made it about the wrong one.
    /// </summary>
    public void Reactivate() => DeactivatedAt = null;

    private static string Named(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();

        return trimmed.Length switch
        {
            0 => throw new ArgumentException("A user needs a name.", nameof(name)),
            > NameLimit => throw new ArgumentException(
                $"A user's name is at most {NameLimit} characters.", nameof(name)),
            _ => trimmed,
        };
    }

    // A user whose password hash is empty is a user anybody could be. There is
    // no state in this product where that is intended, so it is refused here
    // rather than guarded against in every path that reads one.
    private static string Hashed(string passwordHash, string parameterName) =>
        string.IsNullOrWhiteSpace(passwordHash)
            ? throw new ArgumentException("A user needs a password hash.", parameterName)
            : passwordHash;
}
