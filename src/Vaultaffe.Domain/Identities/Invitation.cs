using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.Identities;

/// <summary>
/// Somebody an administrator has asked to join this organization
/// (Specification §6.1): an address, what they will be called, whether they will
/// administer the organization — and a code that turns all three into a user.
/// </summary>
/// <remarks>
/// <b>The instance sends no email.</b> An invitation is a link an administrator
/// copies and hands over, which is the price of having no external dependency to
/// operate (§4). So this row is not a message that was sent; it is a credential
/// that was made, and it behaves like every other credential here: the row holds
/// the hash and never the code, it works exactly once, and it stops working on
/// its own.
/// <para>
/// Seventy-two hours, which is this product's one window — the same one a
/// deletion is recoverable inside (§6.5). A second number would be a second thing
/// to explain, and an invitation nobody used in three days is one an
/// administrator should hand out again rather than one that should still work.
/// </para>
/// </remarks>
public sealed class Invitation : IBelongToAnOrganization
{
    /// <summary>How long an invitation link is good for.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    public Invitation(
        Guid id,
        Guid organizationId,
        string email,
        string name,
        bool isAdministrator,
        byte[] codeHash,
        Guid invitedByUserId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(codeHash);

        Id = id;
        OrganizationId = organizationId;
        Email = EmailAddress.Require(email, nameof(email));
        Name = Named(name);
        IsAdministrator = isAdministrator;
        CodeHash = codeHash;
        InvitedByUserId = invitedByUserId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Write one out: the row, and the code that exists exactly once. The code is
    /// returned rather than stored, for the reason a token value is.
    /// </summary>
    public static (Invitation Invitation, string Code) Write(
        Guid id,
        Guid organizationId,
        string email,
        string name,
        bool isAdministrator,
        Guid invitedByUserId,
        DateTimeOffset now)
    {
        var code = InvitationCode.Issue();

        return (
            new Invitation(
                id,
                organizationId,
                email,
                name,
                isAdministrator,
                InvitationCode.Hash(code),
                invitedByUserId,
                now,
                now + Lifetime),
            code);
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// The address this invitation is for, normalized. The person accepting does
    /// not get to change it: an invitation to one address that creates a user at
    /// another is not an invitation.
    /// </summary>
    public string Email { get; private set; }

    /// <summary>What the administrator called them. The person may correct it when they accept.</summary>
    public string Name { get; private set; }

    /// <summary>Whether accepting this makes an administrator (§6.4).</summary>
    public bool IsAdministrator { get; private set; }

    /// <summary>The hash of the code in the link, and never the code.</summary>
    public byte[] CodeHash { get; private set; }

    /// <summary>Who asked. An invitation is an act of a person, and the list says whose.</summary>
    public Guid InvitedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When somebody signed up with it, and who they turned out to be.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    /// <summary>When an administrator took it back.</summary>
    public DateTimeOffset? WithdrawnAt { get; private set; }

    /// <summary>Where this invitation has got to at <paramref name="moment"/>.</summary>
    /// <remarks>
    /// The order is the order of what already happened: an accepted invitation
    /// stays accepted after it expires, and one nobody used in time is expired
    /// rather than open — which is what makes a link left in a chat window worth
    /// nothing three days later.
    /// </remarks>
    public InvitationState StateAt(DateTimeOffset moment) =>
        AcceptedAt is not null ? InvitationState.Accepted
        : WithdrawnAt is not null ? InvitationState.Withdrawn
        : moment >= ExpiresAt ? InvitationState.Expired
        : InvitationState.Open;

    /// <summary>
    /// Somebody used it. Only an open one can be accepted, so a link that was
    /// used, withdrawn or left too long creates nothing.
    /// </summary>
    public bool AcceptBy(Guid userId, DateTimeOffset moment)
    {
        if (StateAt(moment) is not InvitationState.Open)
        {
            return false;
        }

        AcceptedAt = moment;
        AcceptedByUserId = userId;

        return true;
    }

    /// <summary>An administrator takes it back before anybody used it.</summary>
    public bool Withdraw(DateTimeOffset moment)
    {
        if (StateAt(moment) is not InvitationState.Open)
        {
            return false;
        }

        WithdrawnAt = moment;

        return true;
    }

    private static string Named(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();

        return trimmed.Length switch
        {
            0 => throw new ArgumentException("An invitation needs a name.", nameof(name)),
            > User.NameLimit => throw new ArgumentException(
                $"A name is at most {User.NameLimit} characters.", nameof(name)),
            _ => trimmed,
        };
    }
}
