using Vaultaffe.Infrastructure.Identity;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// Argon2id, on its own. It is here rather than in the unit tests for the reason
/// the envelope is: what it reads is a layer that carries a package, not a rule
/// of the domain — and each of these costs 64 MiB and a moment, which is not what
/// "runs in seconds and needs nothing installed" is for.
/// </summary>
public sealed class PasswordHasherTests
{
    private static readonly Argon2idPasswordHasher _hasher = new();

    private const string Password = "correct horse battery staple";

    [Fact]
    public async Task A_password_verifies_against_its_own_hash()
    {
        var encoded = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);

        Assert.True(await _hasher.VerifyAsync(
            encoded, Password, TestContext.Current.CancellationToken));

        Assert.False(await _hasher.VerifyAsync(
            encoded, Password + "!", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A salt per password, so that two people who chose the same one do not
    /// share a row anybody could notice.
    /// </summary>
    [Fact]
    public async Task The_same_password_hashes_differently_every_time()
    {
        var first = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);
        var second = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);

        Assert.NotEqual(first, second);
        Assert.True(await _hasher.VerifyAsync(
            second, Password, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The encoded value carries its own parameters, which is what makes raising
    /// the cost a re-hash on the next sign-in rather than a migration.
    /// </summary>
    [Fact]
    public async Task The_hash_says_what_made_it()
    {
        var encoded = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);

        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=1$", encoded, StringComparison.Ordinal);
    }

    /// <summary>
    /// Anything this build cannot make sense of is a sign-in that did not work,
    /// not a crashed request: a hash from another product, a truncated column, a
    /// row somebody edited, parameters that would ask this process for four
    /// gigabytes.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$argon2i$v=19$m=65536,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
    [InlineData("$argon2id$v=19$m=4194304,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=1$not-base64$also-not")]
    public async Task An_unreadable_hash_is_a_failed_sign_in(string encoded) =>
        Assert.False(await _hasher.VerifyAsync(
            encoded, Password, TestContext.Current.CancellationToken));

    /// <summary>
    /// The decoy is what a sign-in for an unknown address is checked against, so
    /// that it costs the same as a real one — and it must never be something a
    /// password could match.
    /// </summary>
    [Fact]
    public async Task The_decoy_is_readable_and_matches_nothing()
    {
        Assert.False(await _hasher.VerifyAsync(
            _hasher.DecoyHash, Password, TestContext.Current.CancellationToken));

        Assert.False(await _hasher.VerifyAsync(
            _hasher.DecoyHash, string.Empty, TestContext.Current.CancellationToken));

        Assert.StartsWith(
            "$argon2id$v=19$m=65536,t=3,p=1$", _hasher.DecoyHash, StringComparison.Ordinal);
    }
}
