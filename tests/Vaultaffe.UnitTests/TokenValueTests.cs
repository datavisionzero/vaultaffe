using System.Text;
using System.Text.RegularExpressions;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.UnitTests;

/// <summary>
/// The token format of Specification §5. It is fixed in the first design pass
/// because it cannot be changed afterwards (ADR 0004), so these tests are less a
/// check on the code than the written form of the format itself: a change that
/// makes one of them fail is a change nobody can migrate.
/// </summary>
public sealed class TokenValueTests
{
    [Theory]
    [InlineData(TokenKind.Session, "vaultaffe_session_")]
    [InlineData(TokenKind.Service, "vaultaffe_service_")]
    [InlineData(TokenKind.Agent, "vaultaffe_agent_")]
    public void Each_kind_has_its_own_prefix_and_it_never_changes(TokenKind kind, string prefix)
    {
        Assert.Equal(prefix, TokenValue.PrefixOf(kind));
        Assert.StartsWith(prefix, TokenValue.Issue(kind).Reveal(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The point of the prefix (Specification §5): a scanner finds a token in a
    /// repository or a log, and the server knows what it is talking to before it
    /// looks anything up. A pattern nobody checks is a pattern that drifts from
    /// what is issued.
    /// </summary>
    [Theory]
    [InlineData(TokenKind.Session)]
    [InlineData(TokenKind.Service)]
    [InlineData(TokenKind.Agent)]
    public void A_secret_scanner_pattern_matches_what_is_issued(TokenKind kind)
    {
        var found = Regex.Match(
            $"VAULTAFFE_TOKEN={TokenValue.Issue(kind).Reveal()} # in an .env somebody committed",
            TokenValue.ScannerPattern);

        Assert.True(found.Success);
        Assert.StartsWith(TokenValue.PrefixOf(kind), found.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_carries_two_hundred_and_fifty_six_bits_of_randomness()
    {
        var issued = TokenValue.Issue(TokenKind.Agent);
        var random = issued.Reveal()[TokenValue.PrefixOf(TokenKind.Agent).Length..];

        Assert.Equal(32, TokenValue.RandomBytes);
        Assert.Equal(TokenValue.RandomLength, random.Length);

        // base64url and nothing else: a token has to survive a URL, a shell line
        // and an environment variable without one escaping rule.
        Assert.Matches("^[A-Za-z0-9_-]+$", random);
    }

    [Fact]
    public void Two_tokens_are_not_the_same_token() =>
        Assert.NotEqual(
            TokenValue.Issue(TokenKind.Agent).Reveal(),
            TokenValue.Issue(TokenKind.Agent).Reveal());

    [Fact]
    public void What_was_issued_parses_back_as_the_kind_it_was_issued_for()
    {
        foreach (var kind in Enum.GetValues<TokenKind>())
        {
            var issued = TokenValue.Issue(kind);

            Assert.True(TokenValue.TryParse(issued.Reveal(), out var parsed));
            Assert.Equal(kind, parsed.Kind);
            Assert.Equal(issued.Reveal(), parsed.Reveal());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hunter2")]
    // The right shape under a prefix this product does not issue.
    [InlineData("vaultaffe_admin_ZXhhbXBsZS1vbmx5LW5vdC1hLXRva2VuLTMyLWJ5dGVz")]
    // Doppler's, so that a token pasted from the product we are close to is
    // refused rather than half-recognized.
    [InlineData("dp.st.dev.notatokenofours")]
    public void Something_that_is_not_a_token_is_not_read_as_one(string? presented) =>
        Assert.False(TokenValue.TryParse(presented, out _));

    /// <summary>
    /// Exactness, for the reason `SecretName` spells out: a value that arrives
    /// out of a file or a shell with a newline on the end is a different string,
    /// and a parser that trims is a format that loosens itself over time.
    /// </summary>
    [Theory]
    [InlineData("\n")]
    [InlineData(" ")]
    [InlineData("=")]
    [InlineData("x")]
    public void A_token_with_something_appended_is_not_that_token(string appended)
    {
        var issued = TokenValue.Issue(TokenKind.Session).Reveal();

        Assert.False(TokenValue.TryParse(issued + appended, out _));
        Assert.False(TokenValue.TryParse(appended + issued, out _));
    }

    [Fact]
    public void A_prefix_of_one_kind_over_the_body_of_another_is_still_a_token()
    {
        // Nothing binds the random part to the prefix, and nothing should: the
        // prefix says what the server should look for, the database says what the
        // token is. This is here so that the absence is deliberate rather than
        // discovered later.
        var body = TokenValue.Issue(TokenKind.Agent)
            .Reveal()[TokenValue.PrefixOf(TokenKind.Agent).Length..];

        Assert.True(
            TokenValue.TryParse(TokenValue.PrefixOf(TokenKind.Service) + body, out var parsed));
        Assert.Equal(TokenKind.Service, parsed.Kind);
    }

    /// <summary>
    /// The instance keeps the hash and looks a token up by exactly it
    /// (<c>docs/storage.md</c>), so it has to be the same every time and 32 bytes
    /// wide, which is what <c>ux_token_value_hash</c> indexes.
    /// </summary>
    [Fact]
    public void The_hash_is_what_is_stored_and_it_is_deterministic()
    {
        var issued = TokenValue.Issue(TokenKind.Service);

        Assert.True(TokenValue.TryParse(issued.Reveal(), out var again));
        Assert.Equal(issued.Hash(), again.Hash());
        Assert.Equal(32, issued.Hash().Length);
        Assert.NotEqual(issued.Hash(), TokenValue.Issue(TokenKind.Service).Hash());
    }

    /// <summary>
    /// Specification §6.5 makes a value in a log line a bug rather than an
    /// untidiness. A credential whose interpolation prints itself is how that bug
    /// gets written by accident, so the value leaves through
    /// <c>Reveal</c> and nowhere else.
    /// </summary>
    [Fact]
    public void Printing_a_token_does_not_print_the_token()
    {
        var issued = TokenValue.Issue(TokenKind.Agent);

        Assert.DoesNotContain(
            issued.Reveal(), $"a log line about {issued}", StringComparison.Ordinal);
        Assert.Equal("vaultaffe_agent_…", issued.ToString());
    }

    /// <summary>
    /// Minting is where "stored is the hash, never the token" is either true or
    /// not: the record that goes into the database carries the hash of the value
    /// that was handed out, and carries no way back to it.
    /// </summary>
    [Fact]
    public void Minting_a_token_stores_its_hash_and_hands_the_value_over_once()
    {
        var (token, value) = Token.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            TokenKind.Agent,
            "the agent in this terminal",
            Scopes.Everything,
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(value.Hash(), token.ValueHash);
        Assert.Equal(TokenKind.Agent, value.Kind);
        Assert.NotEqual(Encoding.UTF8.GetBytes(value.Reveal()), token.ValueHash);
    }
}
