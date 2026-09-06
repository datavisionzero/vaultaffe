using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Refusals;

namespace Vaultaffe.Application.Acts;

/// <summary>
/// What a person changes about themselves: their name, their password, and the
/// address they sign in with.
/// </summary>
/// <remarks>
/// **Only a session may.** A service or an agent token names the person who is
/// accountable for it, and renaming or re-crediting that person from inside a
/// process they handed a credential to is not something the token was given for
/// — the same line that makes signing out a session's own act
/// (<c>docs/api.md</c>). It is refused here rather than declared on the endpoint,
/// because it is not one of the short list of §6.4: changing your own name is
/// nobody's administration but your own.
/// </remarks>
public sealed class RenameMyself(
    IIdentityStore identities, ICallerIdentity caller, ChangeLog log)
{
    public async Task<UserRow> ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        var acting = OnlyAPerson(caller);

        var trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length is 0 or > User.NameLimit)
        {
            throw Refusal.Validation(
                "name", $"A name is between 1 and {User.NameLimit} characters.");
        }

        var user = await identities.FindUserAsync(acting.UserId, cancellationToken)
            ?? throw Refusal.NotFound("Nobody here by that id.");

        // What this log calls somebody is itself a fact about the log, so the
        // change to it belongs in it. Recorded by their address rather than by
        // either name: the address is what does not change here (ADR 0020).
        user.RenameTo(trimmed);

        log.Record(ChangeAction.PersonRenamed, about: user.Email);

        await identities.SaveAsync(cancellationToken);

        return ListUsers.Row(user);
    }

    internal static Caller OnlyAPerson(ICallerIdentity caller)
    {
        var acting = caller.Required;

        return acting.IsHumanSession
            ? acting
            : throw Refusal.Forbidden(
                "Only a session changes the person behind it. A service or agent token names "
                + "whoever is accountable for it, and this is theirs to change under their own "
                + "session.");
    }
}

/// <summary>
/// Changing your own password: the old one, and the new one
/// (Specification §6.1).
/// </summary>
/// <remarks>
/// The current password is asked for because a session left open on a borrowed
/// machine should not be enough to lock its owner out of their own account. The
/// wrong one is <c>unauthenticated</c>, the same refusal a sign-in gives, and it
/// says nothing else.
/// <para>
/// It **ends every other session of that person** and keeps this one. Somebody
/// changing a password because they think it is known elsewhere expects exactly
/// that; somebody changing it for tidiness loses nothing but a second browser
/// they can sign back into. Service and agent tokens are untouched: those never
/// depended on the password.
/// </para>
/// </remarks>
public sealed class ChangeMyPassword(
    IIdentityStore identities,
    IPasswordHasher passwords,
    ICallerIdentity caller,
    ChangeLog log,
    TimeProvider clock)
{
    public async Task ExecuteAsync(
        string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var acting = RenameMyself.OnlyAPerson(caller);

        var user = await identities.FindUserAsync(acting.UserId, cancellationToken)
            ?? throw Refusal.NotFound("Nobody here by that id.");

        var correct = await passwords.VerifyAsync(
            user.PasswordHash, currentPassword ?? string.Empty, cancellationToken);

        if (!correct)
        {
            throw Refusal.Unauthenticated("That is not your current password.");
        }

        if (!Password.IsValid(newPassword))
        {
            throw Refusal.Validation(
                "newPassword",
                $"A password is between {Password.MinimumLength} and "
                + $"{Password.MaximumLength} characters.");
        }

        user.ResetPasswordTo(await passwords.HashAsync(newPassword, cancellationToken));

        var now = clock.GetUtcNow();

        foreach (var session in await identities.ListSessionsOfAsync(user.Id, cancellationToken))
        {
            if (session.Id != acting.TokenId)
            {
                session.RevokeAt(now);
            }
        }

        // The same action an administrator's reset writes. Who did it is already
        // on the row, and here it is the person themselves — which is the useful
        // distinction and the one the entry already makes.
        log.Record(ChangeAction.PasswordSet, about: user.Email);

        await identities.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Changing the address you sign in with yourself: the new one, and the password
/// you have (Specification §6.1).
/// </summary>
/// <remarks>
/// The second way to the same column. An administrator can do this for anybody
/// (<see cref="ChangeEmail"/>) because somebody has to be able to repair a
/// lock-out; this is the way that does not need one, and it is where a person's
/// own name and own password already live.
/// <para>
/// The current password is asked for, for the reason it is asked for when
/// changing a password: a session left open on a borrowed machine should not be
/// enough to lock its owner out of their own account — and taking the address
/// away is exactly that, because the instance sends no mail and there is no link
/// back. The wrong one is <c>unauthenticated</c> and says nothing else.
/// </para>
/// <para>
/// <b>Every session stays</b>, this one and the others. An address is not a
/// credential, nothing was compromised by changing it, and every token names its
/// person by id.
/// </para>
/// </remarks>
public sealed class ChangeMyEmail(
    IIdentityStore identities,
    IPasswordHasher passwords,
    ICallerIdentity caller,
    ChangeLog log)
{
    public async Task<UserRow> ExecuteAsync(
        string email, string currentPassword, CancellationToken cancellationToken)
    {
        var acting = RenameMyself.OnlyAPerson(caller);

        var user = await identities.FindUserAsync(acting.UserId, cancellationToken)
            ?? throw Refusal.NotFound("Nobody here by that id.");

        var correct = await passwords.VerifyAsync(
            user.PasswordHash, currentPassword ?? string.Empty, cancellationToken);

        if (!correct)
        {
            throw Refusal.Unauthenticated("That is not your current password.");
        }

        await ChangeEmail.ApplyAsync(identities, user, email, cancellationToken);

        log.Record(ChangeAction.EmailChanged, about: user.Email);

        await identities.SaveAsync(cancellationToken);

        return ListUsers.Row(user);
    }
}
