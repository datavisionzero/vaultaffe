using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.UnitTests;

/// <summary>
/// What a secret is between writes: a placeholder a human still has to fill, or
/// a value that rests sealed and never half-sealed.
/// </summary>
public sealed class SecretTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static Secret ASecret() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "STRIPE_KEY", _now);

    // Bytes that stand for an envelope rather than being one. What the sealing
    // actually produces is Infrastructure's, and `EnvelopeTests` reads it there;
    // here they only have to be told apart.
    private static SealedValue Sealed(byte[] wrappedDataKey, byte[] nonce, byte[] ciphertext) =>
        new(wrappedDataKey, nonce, ciphertext);

    /// <summary>
    /// Specification §6.2: an empty placeholder means a human still has to do
    /// something, and it is what makes `run` refuse to start and name the key.
    /// Silently injecting an empty string would hide exactly that, so the state
    /// is a state and not an empty value.
    /// </summary>
    [Fact]
    public void A_new_secret_is_an_empty_placeholder()
    {
        var secret = ASecret();

        Assert.True(secret.IsPlaceholder);
        Assert.Null(secret.Ciphertext);
        Assert.Null(secret.ValueWrittenAt);
    }

    [Fact]
    public void Sealing_over_a_placeholder_supersedes_nothing()
    {
        var secret = ASecret();

        Assert.Null(secret.Seal(Guid.NewGuid(), Sealed([1], [2], [3]), _now));
        Assert.False(secret.IsPlaceholder);
        Assert.Equal(_now, secret.ValueWrittenAt);
    }

    [Fact]
    public void Sealing_over_a_value_hands_the_old_one_back_rather_than_losing_it()
    {
        var secret = ASecret();
        secret.Seal(Guid.NewGuid(), Sealed([1], [2], [3]), _now);

        var superseded = secret.Seal(Guid.NewGuid(), Sealed([1], [4], [5]), _now.AddHours(1));

        Assert.NotNull(superseded);
        Assert.Equal<byte[]>([3], superseded.Ciphertext);
        Assert.Equal<byte[]>([2], superseded.Nonce);
        Assert.Equal(_now, superseded.WrittenAt);
        Assert.Equal(_now.AddHours(1), superseded.ReplacedAt);
    }

    /// <summary>
    /// One data key per secret, for its lifetime (Specification §6.3). A key that
    /// changed per value would strand every retained version sealed under the old
    /// one — and would make a master-key rotation a re-encryption again, which is
    /// the thing the envelope exists to avoid.
    /// </summary>
    [Fact]
    public void A_secret_keeps_one_data_key()
    {
        var secret = ASecret();
        secret.Seal(Guid.NewGuid(), Sealed([1], [2], [3]), _now);

        Assert.Throws<InvalidOperationException>(() =>
            secret.Seal(Guid.NewGuid(), Sealed([99], [4], [5]), _now.AddHours(1)));
    }

    /// <summary>
    /// The retention clock runs from when a version stopped being current, not
    /// from when it was written: a value that stood for a year and was replaced
    /// this morning is this morning's undo (Specification §6.5).
    /// </summary>
    [Fact]
    public void A_superseded_version_expires_from_the_moment_it_was_replaced()
    {
        var secret = ASecret();
        secret.Seal(Guid.NewGuid(), Sealed([1], [2], [3]), _now.AddYears(-1));

        var superseded = secret.Seal(Guid.NewGuid(), Sealed([1], [4], [5]), _now)!;

        Assert.Equal(_now + ValueHistory.Window, superseded.ExpiresAt);
    }

    /// <summary>
    /// The same rule the database holds in <c>ck_secret_value_sealed_whole</c>:
    /// a ciphertext without the nonce and data key it was sealed under is a value
    /// nobody can open again, and it should not be constructible either.
    /// </summary>
    [Fact]
    public void A_value_is_sealed_whole_or_not_at_all()
    {
        Assert.Throws<ArgumentException>(() => new SealedValue([1], [2], []));
        Assert.Throws<ArgumentException>(() => new SealedValue([1], [], [3]));
        Assert.Throws<ArgumentException>(() => new SealedValue([], [2], [3]));
        Assert.Throws<ArgumentNullException>(() => new SealedValue([1], [2], null!));
    }

    /// <summary>
    /// Deleting is recoverable, and repeating it does not extend the deadline
    /// (Specification §6.5).
    /// </summary>
    [Fact]
    public void Deleting_twice_does_not_move_the_deadline()
    {
        var secret = ASecret();

        secret.DeleteAt(_now);
        secret.DeleteAt(_now.AddHours(10));

        Assert.Equal(_now, secret.DeletedAt);
    }

    /// <summary>
    /// The numbers of Specification §6.5, in the one place they are written down.
    /// This test is here so that changing them is a deliberate act rather than a
    /// typo nobody notices.
    /// </summary>
    [Fact]
    public void The_bounds_are_five_versions_and_seventy_two_hours()
    {
        Assert.Equal(5, ValueHistory.Versions);
        Assert.Equal(TimeSpan.FromHours(72), ValueHistory.Window);
        Assert.Equal(TimeSpan.FromHours(72), ValueHistory.RecoveryWindow);
    }
}
