using Vaultaffe.Application.Acts;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Api.Http;

/// <summary>The caller, as <c>GET /api/v1/me</c> answers it.</summary>
public sealed record MeShape(
    Guid OrganizationId,
    Guid UserId,
    string Name,
    bool IsAdministrator,
    Guid TokenId,
    string? TokenName,
    string TokenKind,
    IReadOnlyList<string> Scopes);

/// <summary>A session and the token it produced, shown exactly once.</summary>
public sealed record SessionShape(
    Guid UserId,
    string Name,
    string Email,
    bool IsAdministrator,
    string Token,
    DateTimeOffset ExpiresAt);

/// <summary>One project, or one environment of it, that a token may touch.</summary>
public sealed record BindingShape(Guid ProjectId, Guid? EnvironmentId);

/// <summary>A token as every listing shows it — everything about it except its value.</summary>
public sealed record TokenShape(
    Guid Id,
    string Kind,
    string? Name,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<BindingShape> Bindings,
    bool ReachesTheWholeOrganization,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);

/// <summary>A token as it is created: the row, and the value nobody will see again.</summary>
public sealed record TokenIssuedShape(TokenShape Token, string Value);

/// <summary>
/// How the domain's words are spelled on the wire.
/// </summary>
/// <remarks>
/// A token kind is spelled here. A scope is not: its words are the
/// specification's own and a refusal has to spell them the same way the contract
/// does, so they live in <see cref="ScopeNames"/> and this file reads which
/// direction a caller wanted.
/// </remarks>
public static class Shapes
{
    public static string KindOf(TokenKind kind) => kind switch
    {
        TokenKind.Session => "session",
        TokenKind.Service => "service",
        TokenKind.Agent => "agent",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "There are three token kinds."),
    };

    public static TokenKind KindOf(string? kind) => kind switch
    {
        "session" => TokenKind.Session,
        "service" => TokenKind.Service,
        "agent" => TokenKind.Agent,
        _ => throw Refusal.Validation(
            "kind", "A token is one of 'service' and 'agent'; a session comes from signing in."),
    };

    /// <summary>The set, as the words a client reads. Ordered as the scopes are declared.</summary>
    public static IReadOnlyList<string> ScopesOf(Scopes scopes) => ScopeNames.NamesOf(scopes);

    /// <summary>The words a client sent, as the set. Null when it sent none.</summary>
    public static Scopes? ScopesOf(IReadOnlyList<string>? scopes)
    {
        if (scopes is null)
        {
            return null;
        }

        var set = Domain.Tokens.Scopes.None;

        foreach (var scope in scopes)
        {
            set |= ScopeNames.Parse(scope)
                ?? throw Refusal.Validation(
                    "scopes", "A scope is one of 'names', 'read', 'write' and 'delete'.");
        }

        return set;
    }

    public static MeShape Me(Caller caller) =>
        new(
            caller.OrganizationId,
            caller.UserId,
            caller.UserName,
            caller.IsAdministrator,
            caller.TokenId,
            caller.TokenName,
            KindOf(caller.TokenKind),
            ScopesOf(caller.Scopes));

    public static SessionShape Session(SignedIn signedIn) =>
        new(
            signedIn.UserId,
            signedIn.Name,
            signedIn.Email,
            signedIn.IsAdministrator,
            signedIn.SessionToken,
            signedIn.ExpiresAt);

    public static TokenShape Token(TokenRow row) =>
        new(
            row.Id,
            KindOf(row.Kind),
            row.Name,
            ScopesOf(row.Scopes),
            [.. row.Bindings.Select(binding =>
                new BindingShape(binding.ProjectId, binding.EnvironmentId))],
            row.ReachesTheWholeOrganization,
            row.CreatedAt,
            row.ExpiresAt,
            row.RevokedAt);
}
