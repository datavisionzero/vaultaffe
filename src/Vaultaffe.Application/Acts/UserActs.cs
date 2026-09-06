using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Refusals;

namespace Vaultaffe.Application.Acts;

/// <summary>A person of the organization, as every listing shows them.</summary>
public sealed record UserRow(
    Guid Id,
    string Email,
    string Name,
    bool IsAdministrator,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeactivatedAt);

/// <summary>
/// The people of the organization (Specification §6.1).
/// </summary>
/// <remarks>
/// Everybody in an organization sees everybody in it: permissions for humans are
/// deliberately trivial and there is no role model beyond the administrator line
/// (§6.4), so this listing has nothing to narrow. What is not in it is a password
/// hash — the shape below is what the screen and the CLI are allowed to know
/// about a person.
/// </remarks>
public sealed class ListUsers(IIdentityStore identities, ICallerIdentity caller)
{
    public async Task<IReadOnlyList<UserRow>> ExecuteAsync(CancellationToken cancellationToken)
    {
        _ = caller.Required;

        return [.. (await identities.ListUsersAsync(cancellationToken)).Select(Row)];
    }

    internal static UserRow Row(User user) =>
        new(user.Id, user.Email, user.Name, user.IsAdministrator, user.CreatedAt, user.DeactivatedAt);
}

/// <summary>
/// An administrator sets somebody's password (Specification §6.1).
/// </summary>
/// <remarks>
/// This is the whole of "forgot my password" in a product that sends no email
/// (§4): an administrator sets one and hands it over the way they handed over the
/// invitation link. It is deliberately not a self-service flow — a reset link
/// mailed to an address is exactly the external dependency this product does not
/// have.
/// <para>
/// It ends every session that person had. A password that was reset because
/// somebody else may know the old one is worth nothing while the sessions opened
/// with it are still working — so the reset is also the revocation, and the
/// person signs in again with what the administrator gave them.
/// </para>
/// </remarks>
public sealed class ResetPassword(
    IIdentityStore identities,
    IPasswordHasher passwords,
    ICallerIdentity caller,
    ChangeLog log,
    TimeProvider clock)
{
    public async Task<UserRow> ExecuteAsync(
        Guid userId, string password, CancellationToken cancellationToken)
    {
        _ = caller.Required;

        if (!Password.IsValid(password))
        {
            throw Refusal.Validation(
                "password",
                $"A password is between {Password.MinimumLength} and "
                + $"{Password.MaximumLength} characters.");
        }

        var user = await identities.FindUserAsync(userId, cancellationToken)
            ?? throw Refusal.NotFound("Nobody here by that id.");

        user.ResetPasswordTo(await passwords.HashAsync(password, cancellationToken));

        var now = clock.GetUtcNow();

        foreach (var session in await identities.ListSessionsOfAsync(userId, cancellationToken))
        {
            session.RevokeAt(now);
        }

        // The person it was done to, by their address — never the password, and
        // there is nowhere in the entry to put one (ADR 0020).
        log.Record(ChangeAction.PasswordSet, about: user.Email);

        await identities.SaveAsync(cancellationToken);

        return ListUsers.Row(user);
    }
}

/// <summary>
/// An administrator changes the address somebody signs in with
/// (Specification §6.1).
/// </summary>
/// <remarks>
/// People marry, change their name, or move to another address at the same
/// company, and the address here is not a delivery target — the instance sends no
/// mail — but the <b>login name</b>. Without this the only ways out are a second
/// account, which loses everything the first one ever signed, or a write straight
/// into the database.
/// <para>
/// An administrator's, for the reason a reset is one: a mistyped address cannot
/// correct itself, because the correction needs a sign-in that the mistyped
/// address just took away, and there is no mail to send a way back through.
/// </para>
/// <para>
/// <b>Their sessions stay.</b> A reset ends them because a password somebody else
/// may know is worth nothing while the sessions opened with it still work; an
/// address is not a credential and nothing was compromised by changing it. Every
/// token names its person by id, so none of them notices.
/// </para>
/// </remarks>
public sealed class ChangeEmail(
    IIdentityStore identities, ICallerIdentity caller, ChangeLog log)
{
    public async Task<UserRow> ExecuteAsync(
        Guid userId, string email, CancellationToken cancellationToken)
    {
        _ = caller.Required;

        var user = await identities.FindUserAsync(userId, cancellationToken)
            ?? throw Refusal.NotFound("Nobody here by that id.");

        await ApplyAsync(identities, user, email, cancellationToken);

        // The new address, because that is the name everything after this entry
        // is about; the old one is in the entry before it, and `Joined` is what
        // guarantees there is one (ADR 0020).
        log.Record(ChangeAction.EmailChanged, about: user.Email);

        await identities.SaveAsync(cancellationToken);

        return ListUsers.Row(user);
    }

    /// <summary>
    /// What both ways of changing an address do, once whoever asked has been
    /// allowed to: one spelling, nobody else's, and the column set. Shared so
    /// that an administrator's way and a person's own cannot drift apart on the
    /// rule itself — only on who may ask.
    /// </summary>
    internal static async Task ApplyAsync(
        IIdentityStore identities, User user, string email, CancellationToken cancellationToken)
    {
        if (!EmailAddress.IsValid(email))
        {
            throw Refusal.Validation(
                "email",
                "One '@', something on either side of it, no spaces, at most "
                + $"{EmailAddress.Limit} characters.");
        }

        var address = EmailAddress.Normalize(email);

        var held = await identities.FindUserByEmailAsync(address, cancellationToken);

        // Themselves is not a collision: the same address in another spelling
        // normalizes to what they already have, and saying no to that would be
        // refusing to change nothing.
        if (held is not null && held.Id != user.Id)
        {
            throw Refusal.NameTaken(
                "Somebody here already signs in with that address.", bySomethingDeleted: false);
        }

        user.ChangeEmailTo(address);
    }
}

/// <summary>
/// Taking somebody out of the organization, and putting them back
/// (Specification §6.4).
/// </summary>
/// <remarks>
/// Deactivated rather than deleted, so that everything they ever changed keeps an
/// author (§6.5) — the same reason a token is revoked rather than deleted. It
/// takes effect everywhere at once: no token of theirs authenticates any more,
/// their sessions and the agent tokens they are accountable for included.
/// <para>
/// <b>Nobody deactivates themselves.</b> Not because it would be wrong, but
/// because it is the one way an instance can end up with nothing an administrator
/// can sign in with — and this product has no support desk to undo that.
/// </para>
/// </remarks>
public sealed class DeactivateUser(
    IIdentityStore identities, ICallerIdentity caller, ChangeLog log, TimeProvider clock)
{
    public async Task<UserRow> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var acting = caller.Required;

        if (acting.UserId == userId)
        {
            throw Refusal.Forbidden(
                "Deactivating yourself would leave this instance with one administrator fewer "
                + "and nobody able to undo it. Another administrator can do it for you.");
        }

        var user = await identities.FindUserAsync(userId, cancellationToken)
            ?? throw Refusal.NotFound("Nobody here by that id.");

        user.DeactivateAt(clock.GetUtcNow());

        log.Record(ChangeAction.Deactivated, about: user.Email);

        await identities.SaveAsync(cancellationToken);

        return ListUsers.Row(user);
    }
}

/// <summary>Putting somebody back. The undo of a decision about a person.</summary>
public sealed class ReactivateUser(
    IIdentityStore identities, ICallerIdentity caller, ChangeLog log)
{
    public async Task<UserRow> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        _ = caller.Required;

        var user = await identities.FindUserAsync(userId, cancellationToken)
            ?? throw Refusal.NotFound("Nobody here by that id.");

        user.Reactivate();

        log.Record(ChangeAction.Reactivated, about: user.Email);

        await identities.SaveAsync(cancellationToken);

        return ListUsers.Row(user);
    }
}
