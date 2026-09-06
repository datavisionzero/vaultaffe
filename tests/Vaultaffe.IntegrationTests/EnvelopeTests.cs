using System.Security.Cryptography;
using System.Text;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Secrets;
using Vaultaffe.Infrastructure.Encryption;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The envelope of Specification §6.3, on its own: a data key per secret, that
/// key wrapped by the instance master key, and a value that cannot be opened
/// without both. These tests need no Postgres — what they are here rather than
/// in the unit tests is the layer they read, not the machinery they need.
/// </summary>
public sealed class EnvelopeTests
{
    private static readonly MasterKey _master = AMasterKey();

    private static readonly KeyRing _keyRing = new(_master);

    [Fact]
    public void A_value_comes_back_out_of_its_envelope_unchanged()
    {
        var sealedValue = _keyRing.Seal("not-a-real-key", null);

        Assert.Equal("not-a-real-key", _keyRing.Open(sealedValue));
    }

    /// <summary>
    /// Values are not ASCII and not short. A connection string with a password in
    /// it is the one this product exists for, and it has both.
    /// </summary>
    [Theory]
    [InlineData("postgres://app:pässwörd@db:5432/app?sslmode=require")]
    [InlineData("")]
    // Newlines and a trailing one: a key pasted out of a file is a value like
    // any other, and nothing here may tidy it up.
    [InlineData("first line\nsecond line\n")]
    public void Whatever_a_value_contains_survives_the_envelope(string value) =>
        Assert.Equal(value, _keyRing.Open(_keyRing.Seal(value, null)));

    [Fact]
    public void A_sealed_value_looks_nothing_like_the_value()
    {
        var sealedValue = _keyRing.Seal("not-a-real-key", null);

        Assert.DoesNotContain(
            "not-a-real-key",
            Encoding.UTF8.GetString(sealedValue.Ciphertext),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// One data key per secret, kept across every write (Specification §6.3).
    /// That is what later makes rotating the master key a rewrap of the wrapped
    /// data keys rather than a re-encryption of every value in the instance.
    /// </summary>
    [Fact]
    public void A_second_write_reuses_the_secrets_data_key_and_not_its_nonce()
    {
        var first = _keyRing.Seal("first", null);
        var second = _keyRing.Seal("second", first.WrappedDataKey);

        Assert.Equal(first.WrappedDataKey, second.WrappedDataKey);
        Assert.NotEqual(first.Nonce, second.Nonce);

        // Both open, which is the point: the superseded version kept for the
        // rollback of §6.5 is sealed under the same key as the current value.
        Assert.Equal("first", _keyRing.Open(first));
        Assert.Equal("second", _keyRing.Open(second));
    }

    [Fact]
    public void Two_secrets_do_not_share_a_data_key()
    {
        var one = _keyRing.Seal("one", null);
        var other = _keyRing.Seal("other", null);

        Assert.NotEqual(one.WrappedDataKey, other.WrappedDataKey);
    }

    /// <summary>
    /// AES-GCM authenticates as well as encrypts, so a row somebody edited fails
    /// to open rather than opening to something else. This is not a claim to
    /// defend against an attacker with the database — Specification §4 says we do
    /// not — but a value that changed underneath is a fault, and a fault should
    /// be loud.
    /// </summary>
    /// <remarks>
    /// Loud enough to reach the caller, which is the whole of VAULT-31: it is a
    /// refusal with a name, so an operator reads a sentence instead of finding
    /// "Something went wrong on the server" and going to look in a container log.
    /// </remarks>
    [Theory]
    [InlineData("ciphertext")]
    [InlineData("nonce")]
    [InlineData("wrapped data key")]
    public void A_value_somebody_edited_does_not_open(string part)
    {
        var sealedValue = _keyRing.Seal("not-a-real-key", null);
        var edited = Bent(sealedValue, part);

        var refused = Assert.Throws<Refusal>(() => _keyRing.Open(edited));

        Assert.Equal(RefusalCode.SealedValueDamaged, refused.Code);
    }

    /// <summary>
    /// The failure an operator actually hits: the instance came up with a
    /// different key than the one the values were sealed under. It has to say so,
    /// rather than reporting every value as damaged.
    /// </summary>
    [Fact]
    public void A_value_under_another_master_key_says_so()
    {
        var sealedValue = _keyRing.Seal("not-a-real-key", null);
        var elsewhere = new KeyRing(AMasterKey());

        var refused = Assert.Throws<Refusal>(() => elsewhere.Open(sealedValue));

        // The code is what a client switches on and the sentence is what a person
        // reads; the point of this refusal is that it carries both, so both are
        // asserted. And it is *not* the damaged-row code: telling an operator
        // their data is broken when their `.env` is wrong sends them to a restore
        // they do not need.
        Assert.Equal(RefusalCode.MasterKeyMismatch, refused.Code);
        Assert.Contains("different master key", refused.Detail!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every wrapped data key names the key it is under, so that the rotation of
    /// §7 can later tell a rewrapped row from one still waiting — and so that the
    /// wrong `.env` is a sentence rather than a mystery.
    /// </summary>
    [Fact]
    public void A_wrapped_data_key_names_the_master_key_it_is_under()
    {
        var sealedValue = _keyRing.Seal("not-a-real-key", null);

        Assert.Equal(KeyRing.Format, sealedValue.WrappedDataKey[0]);
        Assert.Equal(_master.Id, sealedValue.WrappedDataKey[1..(1 + MasterKey.IdLength)]);
        Assert.NotEqual(_master.Id, AMasterKey().Id);
    }

    /// <summary>
    /// Both layouts carry a leading version, so that a later algorithm is a new
    /// byte rather than a guess about what an old row means.
    /// </summary>
    [Fact]
    public void A_layout_this_version_does_not_know_is_refused()
    {
        var sealedValue = _keyRing.Seal("not-a-real-key", null);
        var fromTheFuture = new SealedValue(
            sealedValue.WrappedDataKey,
            sealedValue.Nonce,
            [(byte)(KeyRing.Format + 1), .. sealedValue.Ciphertext[1..]]);

        var refused = Assert.Throws<Refusal>(() => _keyRing.Open(fromTheFuture));

        Assert.Equal(RefusalCode.SealedValueDamaged, refused.Code);
    }

    /// <summary>
    /// An instance without a usable master key does not start (§6.3). The
    /// alternative is one that runs, takes values for an hour and cannot open a
    /// single one of them after the next restart.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 at all")]
    // Right encoding, sixteen bytes: the mistake somebody makes with `head -c 16`.
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    public void An_instance_without_a_usable_master_key_does_not_start(string? configured)
    {
        var refused = Assert.Throws<InvalidOperationException>(() =>
            MasterKey.FromConfiguredValue(configured, "Vaultaffe__MasterKey"));

        Assert.Contains("Vaultaffe__MasterKey", refused.Message, StringComparison.Ordinal);
        Assert.Contains("openssl rand -base64 32", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_key_an_operator_generates_is_the_key_that_is_read()
    {
        var generated = RandomNumberGenerator.GetBytes(MasterKey.Length);

        var read = MasterKey.FromConfiguredValue(
            Convert.ToBase64String(generated), "Vaultaffe__MasterKey");

        Assert.Equal(new MasterKey(generated).Id, read.Id);
    }

    private static MasterKey AMasterKey() =>
        new(RandomNumberGenerator.GetBytes(MasterKey.Length));

    private static SealedValue Bent(SealedValue value, string part)
    {
        static byte[] Flip(byte[] bytes, int at) =>
            [.. bytes[..at], (byte)(bytes[at] ^ 0xff), .. bytes[(at + 1)..]];

        return part switch
        {
            // Past the format byte, so that what fails is the authentication tag
            // and not the layout check in front of it.
            "ciphertext" => new SealedValue(
                value.WrappedDataKey, value.Nonce, Flip(value.Ciphertext, 1)),
            "nonce" => new SealedValue(
                value.WrappedDataKey, Flip(value.Nonce, 0), value.Ciphertext),
            "wrapped data key" => new SealedValue(
                Flip(value.WrappedDataKey, 1 + MasterKey.IdLength),
                value.Nonce,
                value.Ciphertext),
            _ => throw new ArgumentOutOfRangeException(nameof(part), part, "No such part."),
        };
    }
}
