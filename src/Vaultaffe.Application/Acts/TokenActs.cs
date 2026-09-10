using Vaultaffe.Application.Ports;
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
/// Every token of this organization, revoked ones included — a revocation list
/// nobody can read is not one.
/// </summary>
/// <remarks>
/// Session tokens are in it: what a person needs after losing a laptop is to see
/// their sessions and revoke one. No listing anywhere carries a value.
/// </remarks>
public sealed class ListTokens(IIdentityStore identities, ICallerIdentity caller)
{
    public async Task<IReadOnlyList<TokenRow>> ExecuteAsync(CancellationToken cancellationToken)
    {
        _ = caller.Required;

        return [.. (await identities.ListTokensAsync(cancellationToken)).Select(CreateToken.Row)];
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
