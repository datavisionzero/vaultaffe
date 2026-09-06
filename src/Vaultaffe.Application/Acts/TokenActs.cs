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
