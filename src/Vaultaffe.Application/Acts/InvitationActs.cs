using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Application.Acts;

/// <summary>An invitation as every listing shows it — everything but the code.</summary>
public sealed record InvitationRow(
    Guid Id,
    string Email,
    string Name,
    bool IsAdministrator,
    InvitationState State,
    Guid InvitedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>An invitation as it is written: the row, and the code nobody will see again.</summary>
public sealed record InvitationWritten(InvitationRow Invitation, string Code);

/// <summary>What a link turns out to be, for whoever is holding it.</summary>
public sealed record InvitationOffer(
    string OrganizationName, string Email, string Name, InvitationState State);

/// <summary>
/// Inviting somebody into the organization (Specification §6.1).
/// </summary>
/// <remarks>
/// The answer carries the code exactly once, for the reason a token's answer
/// does: an administrator copies the link out of that screen and hands it over,
/// and nothing can ask for it again. A lost link is withdrawn and written out
/// anew, which is one act more than storing it would be and one credential fewer
/// that this instance could read back.
/// <para>
/// Inviting is human-only and an administrator's: it is a decision about people
/// (§6.4). Neither is decided here — the endpoint declares it and one middleware
/// enforces it through <c>Authority</c>.
/// </para>
/// </remarks>
public sealed class InviteUser(
    IIdentityStore identities, ICallerIdentity caller, ChangeLog log, TimeProvider clock)
{
    public async Task<InvitationWritten> ExecuteAsync(
        string email,
        string name,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var acting = caller.Required;

        if (!EmailAddress.IsValid(email))
        {
            throw Refusal.Validation("email", "That is not an email address.");
        }

        var named = (name ?? string.Empty).Trim();

        if (named.Length is 0 or > User.NameLimit)
        {
            throw Refusal.Validation(
                "name", $"A name is between 1 and {User.NameLimit} characters.");
        }

        var address = EmailAddress.Normalize(email);
        var now = clock.GetUtcNow();

        if (await identities.FindUserByEmailAsync(address, cancellationToken) is not null)
        {
            throw Refusal.NameTaken(
                "Somebody here already signs in with that address. An administrator resets "
                + "their password rather than inviting them again.",
                bySomethingDeleted: false);
        }

        var open = (await identities.ListInvitationsAsync(cancellationToken))
            .FirstOrDefault(invitation =>
                invitation.Email == address && invitation.StateAt(now) is InvitationState.Open);

        if (open is not null)
        {
            throw Refusal.NameTaken(
                "That address already has an invitation waiting. Withdraw it first — the link "
                + "it produced is the one that works, and two of them would be one too many.",
                bySomethingDeleted: false);
        }

        var (invitation, code) = Invitation.Write(
            Guid.NewGuid(),
            acting.OrganizationId,
            address,
            named,
            isAdministrator,
            acting.UserId,
            now);

        // The address invited, and never the code in the link: that is a
        // credential, and this log holds none (ADR 0015, ADR 0020).
        log.Record(ChangeAction.Invited, about: invitation.Email);

        await identities.AddInvitationAsync(invitation, cancellationToken);

        return new InvitationWritten(Row(invitation, now), code);
    }

    internal static InvitationRow Row(Invitation invitation, DateTimeOffset now) =>
        new(
            invitation.Id,
            invitation.Email,
            invitation.Name,
            invitation.IsAdministrator,
            invitation.StateAt(now),
            invitation.InvitedByUserId,
            invitation.CreatedAt,
            invitation.ExpiresAt);
}

/// <summary>
/// Every invitation of this organization, newest first, in whatever state — an
/// administrator has to be able to see what is outstanding, and what somebody
/// used.
/// </summary>
public sealed class ListInvitations(
    IIdentityStore identities, ICallerIdentity caller, TimeProvider clock)
{
    public async Task<IReadOnlyList<InvitationRow>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        _ = caller.Required;

        var now = clock.GetUtcNow();

        return
        [
            .. (await identities.ListInvitationsAsync(cancellationToken))
                .Select(invitation => InviteUser.Row(invitation, now)),
        ];
    }
}

/// <summary>Taking one back before anybody used it.</summary>
public sealed class WithdrawInvitation(
    IIdentityStore identities, ICallerIdentity caller, ChangeLog log, TimeProvider clock)
{
    public async Task<InvitationRow> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        _ = caller.Required;

        var invitation = await identities.FindInvitationAsync(id, cancellationToken)
            ?? throw Refusal.NotFound("No invitation by that id.");

        var now = clock.GetUtcNow();

        if (!invitation.Withdraw(now))
        {
            throw Refusal.Forbidden(
                "That invitation is not open any more. One that was used, withdrawn or left "
                + "too long cannot be taken back — there is nothing left to take.");
        }

        log.Record(ChangeAction.InvitationWithdrawn, about: invitation.Email);

        await identities.SaveAsync(cancellationToken);

        return InviteUser.Row(invitation, now);
    }
}

/// <summary>
/// What a link is for, read by whoever is holding it and nobody else
/// (<c>docs/api.md</c>).
/// </summary>
/// <remarks>
/// It answers before anybody is authenticated, because the person on the other
/// end of an invitation is exactly somebody this instance has never met. Holding
/// the code is the proof, which is why the state comes back rather than one
/// sentence for four cases: whoever presents it may know whether the invitation
/// they were handed was already used — the credential is theirs. A code nothing
/// here issued is <c>not-found</c> and learns nothing.
/// </remarks>
public sealed class ReadInvitationOffer(IIdentityStore identities, TimeProvider clock)
{
    public async Task<InvitationOffer> ExecuteAsync(
        string? code, CancellationToken cancellationToken)
    {
        var invitation = await Found(identities, code, cancellationToken);

        var organization = await identities.FindTheOrganizationAsync(cancellationToken)
            ?? throw Refusal.NotFound("This instance has not been started.");

        return new InvitationOffer(
            organization.Name,
            invitation.Email,
            invitation.Name,
            invitation.StateAt(clock.GetUtcNow()));
    }

    internal static async Task<Invitation> Found(
        IIdentityStore identities, string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw Refusal.Validation("code", "An invitation link carries a code.");
        }

        return await identities.FindInvitationByCodeHashAsync(
                InvitationCode.Hash(code), cancellationToken)
            ?? throw Refusal.NotFound("This is not an invitation to anything here.");
    }
}

/// <summary>
/// Accepting one: the person sets their own password and is signed in, in one
/// act (Specification §6.1).
/// </summary>
/// <remarks>
/// The address is the invitation's and not the request's — an invitation to one
/// address that created a user at another would not be an invitation. So is the
/// administrator flag: what somebody may do is the administrator's decision, made
/// when they wrote the link, and not something the person accepting types in.
/// <para>
/// The name is theirs to correct, because it is what the change log will call
/// them and the person it names is the one who should spell it.
/// </para>
/// </remarks>
public sealed class AcceptInvitation(
    IIdentityStore identities, IPasswordHasher passwords, ChangeLog log, TimeProvider clock)
{
    public async Task<SignedIn> ExecuteAsync(
        string? code, string? name, string password, CancellationToken cancellationToken)
    {
        var invitation = await ReadInvitationOffer.Found(identities, code, cancellationToken);
        var now = clock.GetUtcNow();

        if (invitation.StateAt(now) is not InvitationState.Open)
        {
            throw Refusal.Forbidden(
                "This invitation cannot be accepted any more: it was used, withdrawn, or left "
                + "longer than an invitation lives. An administrator writes out a new one.");
        }

        if (!Password.IsValid(password))
        {
            throw Refusal.Validation(
                "password",
                $"A password is between {Password.MinimumLength} and "
                + $"{Password.MaximumLength} characters.");
        }

        var named = string.IsNullOrWhiteSpace(name) ? invitation.Name : name.Trim();

        if (named.Length > User.NameLimit)
        {
            throw Refusal.Validation(
                "name", $"A name is between 1 and {User.NameLimit} characters.");
        }

        // Between writing the invitation and accepting it, somebody may have been
        // given that address another way. The unique index would say so as a
        // failed write; this says it as a refusal a screen can read.
        if (await identities.FindUserByEmailAsync(invitation.Email, cancellationToken) is not null)
        {
            throw Refusal.NameTaken(
                "Somebody here already signs in with that address.", bySomethingDeleted: false);
        }

        var user = new User(
            Guid.NewGuid(),
            invitation.OrganizationId,
            invitation.Email,
            named,
            await passwords.HashAsync(password, cancellationToken),
            invitation.IsAdministrator,
            now);

        var expiresAt = now + Sessions.Lifetime;

        var (token, value) = Token.Issue(
            Guid.NewGuid(),
            user.OrganizationId,
            user.Id,
            TokenKind.Session,
            name: null,
            Scopes.Everything,
            now,
            expiresAt);

        invitation.AcceptBy(user.Id, now);

        // Recorded under the identity this act just made, because there is no
        // caller: nobody was authenticated on the way in, and the person who
        // joined is who acted. It is also the entry every later one about this
        // person leans on — an address change records the new address because
        // the old one is in the entry before it (ADR 0020).
        log.RecordBy(Caller.Of(user, token), ChangeAction.Joined, about: user.Email);

        await identities.AcceptInvitationAsync(user, token, cancellationToken);

        return new SignedIn(
            user.Id, user.Name, user.Email, user.IsAdministrator, value.Reveal(), expiresAt);
    }
}
