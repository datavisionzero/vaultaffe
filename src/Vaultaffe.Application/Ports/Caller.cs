using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Application.Ports;

/// <summary>
/// Who is calling, as every act wants to know it: the organization, the person
/// behind the request, the token it came in under, and what that token may do.
/// </summary>
/// <remarks>
/// It is a value rather than the rows it was built from, so that an act can be
/// asked about a caller in a unit test without a database, and so that the
/// adapter which authenticated the request hands the same thing to every act in
/// it.
/// <para>
/// <see cref="IdentityType"/> is the one the change log records beside the
/// identity (Specification §6.5), and the token kind is what tells us: an agent
/// acting under its own token is the entry that stops the log from saying a
/// person's name for everything an agent did in their terminal.
/// </para>
/// </remarks>
public sealed record Caller(
    Guid OrganizationId,
    Guid UserId,
    string UserName,
    bool IsAdministrator,
    Guid TokenId,
    string? TokenName,
    TokenKind TokenKind,
    Scopes Scopes,
    TokenReach Reach)
{
    /// <summary>Whether a person is behind this request rather than a machine.</summary>
    public bool IsHumanSession => TokenKind is TokenKind.Session;

    /// <summary>Whether this caller's token carries every scope in <paramref name="wanted"/>.</summary>
    public bool Allows(Scopes wanted) => Scopes.Includes(wanted);

    /// <summary>
    /// Whether this caller's token may touch that project, or that environment
    /// of it. A session reaches its whole organization: the granularity is the
    /// tokens', and the human-only list is what a person is held to (§6.4).
    /// </summary>
    public bool Reaches(Guid projectId, Guid? environmentId = null) =>
        Reach.Covers(projectId, environmentId);

    /// <summary>
    /// Whether this caller's token may touch that project as a whole — the
    /// question renaming, deleting or adding an environment has to ask
    /// (<see cref="TokenReach.CoversAllOf"/>).
    /// </summary>
    public bool ReachesAllOf(Guid projectId) => Reach.CoversAllOf(projectId);

    /// <summary>
    /// What the change log records: the acting identity, and its type. A session
    /// is the person; anything else is the token, so that a revoked token's
    /// entries still read as something other than a bare id.
    /// </summary>
    public (Guid Id, IdentityType Type, string Name) Identity => TokenKind switch
    {
        TokenKind.Session => (UserId, IdentityType.HumanSession, UserName),
        TokenKind.Service => (TokenId, IdentityType.ServiceToken, TokenName ?? "service token"),
        TokenKind.Agent => (TokenId, IdentityType.AgentToken, TokenName ?? "agent token"),
        _ => throw new InvalidOperationException("There are three token kinds."),
    };

    public static Caller Of(User user, Token token) =>
        new(
            token.OrganizationId,
            user.Id,
            user.Name,
            user.IsAdministrator,
            token.Id,
            token.Name,
            token.Kind,
            token.Scopes,
            TokenReach.Of(token));
}

/// <summary>
/// The caller of this request, as the port the acts ask (<c>docs/codebase.md</c>).
/// The HTTP adapter answers it from the request it authenticated; a test answers
/// it with whoever the test says is calling.
/// </summary>
public interface ICallerIdentity
{
    /// <summary>
    /// The authenticated caller, or null on a request nothing has authenticated —
    /// the first run, a sign-in, the handshake.
    /// </summary>
    Caller? Caller { get; }

    /// <summary>
    /// The caller, for an act that has no meaning without one. Refuses rather
    /// than answering with nobody.
    /// </summary>
    Caller Required =>
        Caller ?? throw Domain.Refusals.Refusal.Unauthenticated(
            "This needs a token. Send it as `Authorization: Bearer <token>`.");
}
