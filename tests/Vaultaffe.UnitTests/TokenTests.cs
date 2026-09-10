using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.UnitTests;

/// <summary>
/// The scope set and the binding of Specification §5 and §6.4 — a set rather than
/// a read/write switch, and a default that attributes rather than restricts.
/// </summary>
public sealed class TokenTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static Token AToken(TokenKind kind, Scopes scopes) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), kind, "ci", [1, 2, 3], scopes, _now);

    /// <summary>
    /// The point of a scope set rather than a switch: "may write but never read"
    /// has to be expressible, and in a secrets manager reading is the dangerous
    /// operation rather than the harmless one.
    /// </summary>
    [Fact]
    public void May_write_but_never_read_is_expressible()
    {
        var token = AToken(TokenKind.Agent, Scopes.Names | Scopes.Write);

        Assert.True(token.Allows(Scopes.Write));
        Assert.True(token.Allows(Scopes.Names | Scopes.Write));
        Assert.False(token.Allows(Scopes.Read));
        Assert.False(token.Allows(Scopes.Write | Scopes.Read));
    }

    /// <summary>
    /// Specification §6.4: an agent token's default is the whole organization
    /// with every scope. The point is attribution, not restriction; the human
    /// narrows it at creation.
    /// </summary>
    [Fact]
    public void An_agent_token_reaches_everything_until_a_human_narrows_it()
    {
        var token = AToken(TokenKind.Agent, Scopes.Everything);

        Assert.True(token.ReachesTheWholeOrganization);
        Assert.True(token.Allows(Scopes.Delete));

        token.BindTo(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(token.ReachesTheWholeOrganization);
    }

    /// <summary>A service token's default is narrow: one project, names and read.</summary>
    [Fact]
    public void A_service_token_defaults_to_reading()
    {
        var token = AToken(TokenKind.Service, Scopes.ServiceDefault);

        Assert.True(token.Allows(Scopes.Names | Scopes.Read));
        Assert.False(token.Allows(Scopes.Write));
        Assert.False(token.Allows(Scopes.Delete));
    }

    [Fact]
    public void An_expired_token_authenticates_nothing()
    {
        var token = new Token(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TokenKind.Session, null, [1],
            Scopes.Everything, _now, _now.AddHours(1));

        Assert.True(token.IsUsableAt(_now));
        Assert.False(token.IsUsableAt(_now.AddHours(2)));
    }

    /// <summary>
    /// Revoked rather than deleted, so that everything a token ever signed in the
    /// change log keeps an author — and revoking twice does not move the moment
    /// it happened.
    /// </summary>
    [Fact]
    public void Revoking_is_final_and_dated_once()
    {
        var token = AToken(TokenKind.Agent, Scopes.Everything);

        token.RevokeAt(_now);
        token.RevokeAt(_now.AddHours(5));

        Assert.Equal(_now, token.RevokedAt);
        Assert.False(token.IsUsableAt(_now.AddSeconds(1)));
    }

    /// <summary>
    /// What a token is called, what it may do and how far it reaches can all
    /// change; the value cannot. That asymmetry is the whole reason changing one
    /// is worth having — whatever is holding the token keeps working, because the
    /// string it authenticates with is untouched.
    /// </summary>
    [Fact]
    public void Everything_but_the_value_can_change()
    {
        var token = AToken(TokenKind.Agent, Scopes.Names);
        var hash = token.ValueHash;

        token.RenameTo("the agent on the webshop");
        token.ChangeScopesTo(Scopes.Names | Scopes.Read);

        Assert.Equal("the agent on the webshop", token.Name);
        Assert.Equal(Scopes.Names | Scopes.Read, token.Scopes);
        Assert.Same(hash, token.ValueHash);
    }

    /// <summary>
    /// A reach is laid down again rather than added to, and what was there is
    /// handed back: the rows a change removed are the ones somebody else has to
    /// be told about, and an aggregate that only cleared its own list would leave
    /// nobody able to say which those were.
    /// </summary>
    [Fact]
    public void Unbinding_hands_back_what_it_took_off()
    {
        var token = AToken(TokenKind.Service, Scopes.ServiceDefault);
        var project = Guid.NewGuid();

        var first = token.BindTo(Guid.NewGuid(), project);

        Assert.False(token.ReachesTheWholeOrganization);

        var were = token.Unbind();

        Assert.Equal([first], were);
        Assert.True(token.ReachesTheWholeOrganization);
    }

    /// <summary>
    /// All three kinds exist from day one, even though at first only attribution
    /// tells them apart: a token kind is not added later without a migration
    /// (Specification §5).
    /// </summary>
    [Fact]
    public void There_are_exactly_three_kinds() =>
        Assert.Equal(
            [TokenKind.Session, TokenKind.Service, TokenKind.Agent],
            Enum.GetValues<TokenKind>());
}
