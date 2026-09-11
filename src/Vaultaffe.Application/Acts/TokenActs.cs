using Vaultaffe.Application.Authorization;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Authorization;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Application.Acts;

/// <summary>One project, or one environment of it, a token may touch.</summary>
public sealed record BindingRequest(Guid ProjectId, Guid? EnvironmentId);

/// <summary>A token as a listing shows it — everything about it except its value.</summary>
public sealed record TokenRow(
    Guid Id,
    TokenKind Kind,
    string? Name,
    Scopes Scopes,
    IReadOnlyList<BindingRequest> Bindings,
    bool ReachesTheWholeOrganization,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);

/// <summary>A token as it is created: the row, and the value nobody will see again.</summary>
public sealed record TokenIssued(TokenRow Token, string Value);

/// <summary>
/// Creating a service or an agent token (Specification §6.1, §6.4). The value is
/// shown exactly once, here, and nothing can ask for it afterwards.
/// </summary>
/// <remarks>
/// A default agent token is the whole organization with every scope, because the
/// point is attribution and not restriction (§6.4): an agent acts under its own
/// token so the change log can say an agent acted, and a human narrows it at
/// creation if they want to. A service token defaults to names and read.
/// <para>
/// Session tokens are not created here. One comes out of a sign-in or a device
/// login and nowhere else — a session somebody could mint for another person is
/// not a session.
/// </para>
/// </remarks>
public sealed class CreateToken(
    IIdentityStore identities, ICallerIdentity caller, ChangeLog log, TimeProvider clock)
{
    /// <summary>The longest a token name may be.</summary>
    public const int NameLimit = 100;

    public async Task<TokenIssued> ExecuteAsync(
        TokenKind kind,
        string name,
        Scopes? scopes,
        IReadOnlyList<BindingRequest>? bindings,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        // Creating a token is human-only, and that is not decided here: the
        // endpoint declares `HumanOnly(HumanAction.CreateToken)` and one
        // middleware enforces it through Authority (§6.4). What is left in this
        // act is what a token is, which is its own business.
        var acting = caller.Required;

        if (kind is TokenKind.Session)
        {
            throw Refusal.Validation(
                "kind", "A session token comes from signing in, not from being created.");
        }

        if (kind is not (TokenKind.Service or TokenKind.Agent))
        {
            throw Refusal.Validation("kind", "There are three token kinds.");
        }

        var named = (name ?? string.Empty).Trim();

        if (named.Length is 0 or > NameLimit)
        {
            throw Refusal.Validation(
                "name",
                $"A token needs a name of at most {NameLimit} characters, so that a revocation "
                + "list is readable.");
        }

        var now = clock.GetUtcNow();

        if (expiresAt is not null && expiresAt <= now)
        {
            throw Refusal.Validation("expiresAt", "An expiry in the past creates nothing.");
        }

        var (token, value) = Token.Issue(
            Guid.NewGuid(),
            acting.OrganizationId,
            acting.UserId,
            kind,
            named,
            scopes ?? DefaultScopesOf(kind),
            now,
            expiresAt);

        foreach (var binding in bindings ?? [])
        {
            token.BindTo(Guid.NewGuid(), binding.ProjectId, binding.EnvironmentId);
        }

        // The name a person chose, and never the value: the value exists once, in
        // the answer to this request, and a log line carrying it would be the
        // one leak this product spends every other decision avoiding
        // (§6.5, ADR 0020).
        log.Record(ChangeAction.TokenCreated, about: token.Name);

        await identities.AddTokenAsync(token, cancellationToken);

        return new TokenIssued(Row(token), value.Reveal());
    }

    /// <summary>
    /// What a token of that kind carries unless the human says otherwise
    /// (§6.4). An agent gets everything, a service gets names and read.
    /// </summary>
    public static Scopes DefaultScopesOf(TokenKind kind) => kind switch
    {
        TokenKind.Agent => Scopes.Everything,
        TokenKind.Service => Scopes.ServiceDefault,
        _ => Scopes.None,
    };

    internal static TokenRow Row(Token token) =>
        new(
            token.Id,
            token.Kind,
            token.Name,
            token.Scopes,
            [.. token.Bindings.Select(binding =>
                new BindingRequest(binding.ProjectId, binding.EnvironmentId))],
            token.ReachesTheWholeOrganization,
            token.CreatedAt,
            token.ExpiresAt,
            token.RevokedAt);
}

/// <summary>
/// Every token of this organization that still authenticates, and the revoked
/// ones as well when they are asked for.
/// </summary>
/// <remarks>
/// <b>Revoked is hidden and not dropped.</b> The row stays for good — everything
/// it ever signed in the change log keeps an author that way (§6.5) — and that is
/// a reason to keep it, not a reason to keep it in front of the ones that work.
/// A credential retired months ago is not what anybody opening this list came to
/// see, and on an instance that has been running a while it is most of what they
/// would be shown. So the answer is what works, and the rest is one parameter
/// away: a revocation list nobody can read is not one either.
/// <para>
/// <c>revoked</c> <b>widens rather than switches</b>, which is the one way this
/// differs from <c>deleted</c> on the catalogue's listings. A deleted project is
/// in a bin of its own with a deadline on it, and asking for that bin is asking
/// for something else; a revoked token is a row of this same inventory, read
/// beside the one that replaced it.
/// </para>
/// <para>
/// Session tokens are in it: what a person needs after losing a laptop is to see
/// their sessions and revoke one. No listing anywhere carries a value.
/// </para>
/// </remarks>
public sealed class ListTokens(IIdentityStore identities, ICallerIdentity caller)
{
    public async Task<IReadOnlyList<TokenRow>> ExecuteAsync(
        bool revoked, CancellationToken cancellationToken)
    {
        _ = caller.Required;

        return
        [
            .. (await identities.ListTokensAsync(cancellationToken))
                .Where(token => revoked || token.RevokedAt is null)
                .Select(CreateToken.Row),
        ];
    }
}

/// <summary>
/// Revoking one. Revoked rather than deleted, so that everything it ever signed
/// in the change log keeps an author (§6.5).
/// </summary>
public sealed class RevokeToken(
    IIdentityStore identities, ICallerIdentity caller, ChangeLog log, TimeProvider clock)
{
    public async Task<TokenRow> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        _ = caller.Required;

        var token = await identities.FindTokenAsync(id, cancellationToken)
            ?? throw Refusal.NotFound("No token by that id.");

        token.RevokeAt(clock.GetUtcNow());

        // A session has no name, because nobody gives one to a login. What is
        // revoked is then said by its kind rather than left blank: a row reading
        // "token-revoked, about nothing" would be the entry a person most wants
        // to understand and least can.
        log.Record(
            ChangeAction.TokenRevoked,
            about: token.Name ?? $"a {Words.For(token.Kind)}");

        await identities.SaveAsync(cancellationToken);

        return CreateToken.Row(token);
    }
}

/// <summary>
/// Changing one after it exists: what it is called, what it may do, and how far
/// it reaches (Specification §6.1).
/// </summary>
/// <remarks>
/// <b>The value is not touched.</b> Nothing holding this token has to be told
/// anything: the same string keeps authenticating, and what changed is what the
/// instance lets it through for. That is the whole reason this act is worth
/// having — a name chosen before the agent existed, or a project that has to be
/// reached now, would otherwise mean issuing a second credential and going round
/// every machine that holds the first.
/// <para>
/// <b>And it is why it is human-only</b>, declared by the endpoint as
/// <c>ChangeToken</c>: widening a token hands a wider credential to whoever
/// already holds that value, without issuing anything and without anybody being
/// shown a value. An agent that could do this could do it to its own token.
/// </para>
/// <para>
/// Omitted means unchanged, and that is why the reach is a list that may be
/// empty rather than a flag: <c>null</c> leaves the binding alone, and <c>[]</c>
/// is the deliberate request for the whole organization. At creation the two are
/// the same thing, because there is nothing to leave alone.
/// </para>
/// </remarks>
public sealed class ChangeToken(
    IIdentityStore identities, ICallerIdentity caller, ChangeLog log)
{
    public async Task<TokenRow> ExecuteAsync(
        Guid id,
        string? name,
        Scopes? scopes,
        IReadOnlyList<BindingRequest>? bindings,
        CancellationToken cancellationToken)
    {
        _ = caller.Required;

        var token = await identities.FindTokenAsync(id, cancellationToken)
            ?? throw Refusal.NotFound("No token by that id.");

        // A session is not a thing anybody named or bound; it is what a sign-in
        // left behind, and the way to be rid of one is to revoke it. Letting a
        // person rename a session would put a name in the one list that reads by
        // when it appeared.
        if (token.Kind is TokenKind.Session)
        {
            throw Refusal.Validation(
                "id",
                "A session is not named, scoped or bound by hand: it is what signing in left "
                + "behind. Revoke it instead.");
        }

        // Changing a revoked token would be arranging the reach of something that
        // reaches nothing. Whoever wants it back wants a new one, with a value
        // they will be shown.
        if (token.RevokedAt is not null)
        {
            throw Refusal.Validation(
                "id",
                "That token is revoked and authenticates nothing. Create a new one rather than "
                + "changing this.");
        }

        if (name is null && scopes is null && bindings is null)
        {
            throw Refusal.Validation(
                "name", "Nothing was asked for: send a name, a scope set or a reach.");
        }

        if (name is not null)
        {
            var named = name.Trim();

            if (named.Length is 0 or > CreateToken.NameLimit)
            {
                throw Refusal.Validation(
                    "name",
                    $"A token needs a name of at most {CreateToken.NameLimit} characters, so that "
                    + "a revocation list is readable.");
            }

            token.RenameTo(named);
        }

        if (scopes is not null)
        {
            token.ChangeScopesTo(scopes.Value);
        }

        if (bindings is not null)
        {
            // Which rows go and which arrive is said rather than left to be
            // worked out from the aggregate: a store that had to tell a binding
            // this act just made from one it loaded a moment ago would be
            // guessing, and the wrong guess is an update where an insert
            // belonged.
            foreach (var was in token.Unbind())
            {
                identities.Remove(was);
            }

            foreach (var binding in bindings)
            {
                identities.Add(
                    token.BindTo(Guid.NewGuid(), binding.ProjectId, binding.EnvironmentId));
            }
        }

        // The name it has now, which is the one every entry after this is about —
        // the old one is in the entry before it, exactly as a renamed project's
        // is (ADR 0020). Never the value: there is none to record and there never
        // was after the answer that created it.
        log.Record(ChangeAction.TokenChanged, about: token.Name);

        await identities.SaveAsync(cancellationToken);

        return CreateToken.Row(token);
    }
}

/// <summary>
/// Replacing the value of a token that already exists: the row it had is revoked
/// and a successor is issued beside it, carrying the same name, the same scopes
/// and the same reach. The new value is shown exactly once, in this answer.
/// </summary>
/// <remarks>
/// <b>This is the one thing about a credential that could not be changed.</b>
/// Everything else — what it is called, what it may do, how far it reaches — is
/// <see cref="ChangeToken"/>, and none of it helps when what went wrong is the
/// value itself. A value that has leaked used to mean revoking and creating a
/// second token, and then going round every machine that held the first under a
/// name the log now reads as somebody else.
/// <para>
/// <b>The row is not overwritten.</b> Writing a new hash into the row that is
/// already there is cheaper and erases the only record that anything happened:
/// <c>created_at</c> would go on claiming this credential dates from the day it
/// was first issued, and nothing at all would say when the value in circulation
/// changed. So the old row is revoked and the successor added beside it, and the
/// date is a fact the database holds rather than one nobody wrote down (ADR 0022).
/// </para>
/// <para>
/// <b>The old value is dead the moment this returns</b>, in the same transaction,
/// with no overlap. A rotation is usually the answer to something having gone
/// wrong, and a grace period is exactly as useful to whoever has the leaked value
/// as it is to the run still holding the good one.
/// </para>
/// <para>
/// <b>A fresh expiry, of the length the last one had.</b> A token issued to run
/// for thirty days gets thirty days again; one issued to run until revoked keeps
/// running until revoked. That is what makes this act a renewal and not only a
/// replacement — and it is bounded, because the length is the one a person chose
/// when they issued it and no caller can lengthen it here.
/// </para>
/// <para>
/// <b>Human-only, with one exception.</b> The answer is a value, which is why
/// creating is a person's act; the exception is an agent asking for the successor
/// of the token it is holding itself, which gains it nothing it did not already
/// have and reaches it through a file rather than a screen (ADR 0022). That
/// question is <see cref="Authority.RequiresAHumanOrTheAgentHolding"/>, because
/// it is a question about this object and not about this endpoint.
/// </para>
/// </remarks>
public sealed class RotateToken(
    IIdentityStore identities, Authority authority, ChangeLog log, TimeProvider clock)
{
    public async Task<TokenIssued> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        authority.RequiresAHumanOrTheAgentHolding(id, HumanAction.RotateToken);

        var token = await identities.FindTokenAsync(id, cancellationToken)
            ?? throw Refusal.NotFound("No token by that id.");

        // A session is what signing in left behind, and the way to a new one is
        // to sign in again. Rotating one would hand a browser's credential to
        // whoever asked for it over the API, which is a session for somebody
        // else however it is worded.
        if (token.Kind is TokenKind.Session)
        {
            throw Refusal.Validation(
                "id",
                "A session is not rotated: it is what signing in left behind, and signing in "
                + "again is how a new one is got. Revoke this one instead.");
        }

        // Rotating a revoked token would put a credential back that somebody
        // deliberately took away, under the name the revocation list is still
        // showing. Whoever wants one back wants a new one, created deliberately.
        if (token.RevokedAt is not null)
        {
            throw Refusal.Validation(
                "id",
                "That token is revoked. A rotation replaces a value that works; putting a "
                + "withdrawn credential back is creating one, and that is its own act.");
        }

        var now = clock.GetUtcNow();

        var (successor, value) = Token.Issue(
            Guid.NewGuid(),
            token.OrganizationId,
            // The person accountable for it stays the person accountable for it.
            // A rotation is the same credential with another value, and moving
            // it to whoever happened to ask would quietly rewrite who answers
            // for what an agent does (§6.5).
            token.UserId,
            token.Kind,
            token.Name,
            token.Scopes,
            now,
            // The length the last one was given, starting now. Taking the old
            // moment would hand back a token that expires the same afternoon,
            // and taking no expiry at all would let a caller turn a credential
            // somebody time-boxed into one that never runs out.
            token.ExpiresAt is { } until ? now + (until - token.CreatedAt) : null);

        foreach (var binding in token.Bindings)
        {
            successor.BindTo(Guid.NewGuid(), binding.ProjectId, binding.EnvironmentId);
        }

        token.RevokeAt(now);

        // One entry for one act. Two — a revocation and a creation — would read
        // as two decisions in the log a person scrolls after an incident, and
        // the one fact they are looking for is that the value changed on this
        // day. Never the value: there is one in this answer and nowhere else.
        log.Record(ChangeAction.TokenRotated, about: token.Name ?? $"a {Words.For(token.Kind)}");

        // The successor and the revocation of what it replaces, in one write:
        // this port carries whatever else the act changed on rows it handed out
        // (`IIdentityStore.AddTokenAsync`), so no moment exists in which both
        // work or neither does.
        await identities.AddTokenAsync(successor, cancellationToken);

        return new TokenIssued(CreateToken.Row(successor), value.Reveal());
    }
}

/// <summary>
/// Removing a revoked token's row for good — <c>Purged</c> pointed at a
/// credential rather than at the vault.
/// </summary>
/// <remarks>
/// <b>Only a revoked one.</b> A purge is the second half of a revocation and not
/// a quieter one: a row that vanished while its value still worked would be a
/// credential nobody could find and nobody could take back. Revoking first is
/// what makes the list of revocations honest for as long as anybody might look
/// at it.
/// <para>
/// <b>The change log keeps everything this token signed.</b> It records identities
/// by name and holds no key to this row, which is exactly so that the row can go
/// without taking the history with it (<c>docs/storage.md</c>) — and the purge
/// itself is one more entry, under the name that is about to stop existing.
/// </para>
/// <para>
/// <b>Human-only</b>, declared by the endpoint as <c>PurgeToken</c>, for the
/// reason every purge is: it is the one removal this product cannot undo, and in
/// an agent's hands a credential list it can prune is anti-forensics.
/// </para>
/// </remarks>
public sealed class PurgeToken(IIdentityStore identities, ICallerIdentity caller, ChangeLog log)
{
    public async Task<TokenRow> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        _ = caller.Required;

        var token = await identities.FindTokenAsync(id, cancellationToken)
            ?? throw Refusal.NotFound("No token by that id.");

        if (token.RevokedAt is null)
        {
            throw Refusal.Validation(
                "id",
                "That token still works. Revoke it first: a purge removes what a revocation left "
                + "standing, it is not a quieter way of revoking.");
        }

        // A session has no name, so what went is said by its kind — the same
        // sentence a revocation writes, for the same reason: this is the last
        // entry that will ever mention this row.
        log.Record(
            ChangeAction.TokenPurged,
            about: token.Name ?? $"a {Words.For(token.Kind)}");

        // The row as it last was, which is the only place it exists from here on.
        // Answering with it rather than with nothing is what lets a client say
        // which token went, and it carries no value: there has been none to carry
        // since the answer that created it.
        var row = CreateToken.Row(token);

        await identities.RemoveTokenAsync(token, cancellationToken);

        return row;
    }
}

/// <summary>
/// The word for a token kind, where a sentence needs one. The wire spelling is
/// the API's business (<c>docs/api.md</c>); this is what goes into a change-log
/// entry a person reads.
/// </summary>
internal static class Words
{
    public static string For(TokenKind kind) => kind switch
    {
        TokenKind.Session => "signed-in session",
        TokenKind.Service => "service token",
        TokenKind.Agent => "agent token",
        _ => "token",
    };
}
