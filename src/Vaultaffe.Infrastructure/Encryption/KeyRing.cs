using System.Security.Cryptography;
using System.Text;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.Infrastructure.Encryption;

/// <summary>
/// The envelope of Specification §6.3, and the only place in this product that
/// holds a value in the clear on purpose: a data key per secret seals the value,
/// the instance master key wraps the data key, and neither key is ever written
/// anywhere bare.
/// </summary>
/// <remarks>
/// AES-256-GCM on both levels. It authenticates as well as encrypts, so a
/// ciphertext somebody edited in the database fails to open rather than opening
/// to something else; and it is the one AEAD every machine this runs on
/// accelerates in hardware. The layouts are in ADR 0004, and both carry a
/// leading format byte so that a future algorithm is a new byte rather than a
/// guess about what an old row means.
/// </remarks>
public sealed class KeyRing(MasterKey master) : IKeyRing
{
    /// <summary>AES-256 again: a data key is as long as the key that wraps it.</summary>
    public const int DataKeyLength = 32;

    /// <summary>The version of both layouts below. Read before anything else is.</summary>
    public const byte Format = 1;

    /// <summary>
    /// The 96-bit nonce AES-GCM is specified for. A fresh random one per write
    /// rather than a counter: with a key of its own per secret and a value
    /// rewritten by hand, the number of writes under one key is nowhere near
    /// where the birthday bound would matter, and a counter would need state a
    /// restore from backup could hand out twice.
    /// </summary>
    public const int NonceLength = 12;

    /// <summary>The full 128-bit authentication tag, appended to what it covers.</summary>
    public const int TagLength = 16;

    /// <summary>The format byte and the master key id in front of a wrapped data key.</summary>
    public const int WrappedHeaderLength = 1 + MasterKey.IdLength;

    public SealedValue Seal(string value, byte[]? wrappedDataKey)
    {
        ArgumentNullException.ThrowIfNull(value);

        var dataKey = wrappedDataKey is null
            ? RandomNumberGenerator.GetBytes(DataKeyLength)
            : Unwrap(wrappedDataKey);

        try
        {
            var plaintext = Encoding.UTF8.GetBytes(value);
            var nonce = RandomNumberGenerator.GetBytes(NonceLength);

            // [format][ciphertext][tag]
            var sealedValue = new byte[1 + plaintext.Length + TagLength];
            sealedValue[0] = Format;

            using (var aes = new AesGcm(dataKey, TagLength))
            {
                aes.Encrypt(
                    nonce,
                    plaintext,
                    sealedValue.AsSpan(1, plaintext.Length),
                    sealedValue.AsSpan(1 + plaintext.Length));
            }

            CryptographicOperations.ZeroMemory(plaintext);

            // The wrapped key is kept rather than made again: a secret keeps one
            // data key for its lifetime, and a second wrapping of the same key
            // would be a second row-sized blob saying the same thing.
            return new SealedValue(wrappedDataKey ?? Wrap(dataKey), nonce, sealedValue);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public string Open(SealedValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Ciphertext.Length < 1 + TagLength || value.Ciphertext[0] != Format)
        {
            throw Damaged("This value was not sealed in a layout this version understands.");
        }

        var dataKey = Unwrap(value.WrappedDataKey);

        try
        {
            var body = value.Ciphertext.AsSpan(1);
            var plaintext = new byte[body.Length - TagLength];

            using var aes = new AesGcm(dataKey, TagLength);
            aes.Decrypt(
                value.Nonce, body[..plaintext.Length], body[plaintext.Length..], plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (AuthenticationTagMismatchException)
        {
            // The key opened the data key, so the key is right; what did not
            // verify is the value itself. Nothing of it is in the sentence — a
            // ciphertext that failed to authenticate is still a ciphertext of
            // something (§6.5).
            throw Damaged(
                "This value did not verify under its own data key. The row it is stored in "
                + "has been changed since it was written.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    // [format][master key id][nonce][data key][tag]
    private byte[] Wrap(ReadOnlySpan<byte> dataKey)
    {
        var wrapped = new byte[WrappedHeaderLength + NonceLength + dataKey.Length + TagLength];

        wrapped[0] = Format;
        master.Id.CopyTo(wrapped.AsSpan(1));

        var nonce = wrapped.AsSpan(WrappedHeaderLength, NonceLength);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(master.Bytes, TagLength);
        aes.Encrypt(
            nonce,
            dataKey,
            wrapped.AsSpan(WrappedHeaderLength + NonceLength, dataKey.Length),
            wrapped.AsSpan(WrappedHeaderLength + NonceLength + dataKey.Length));

        return wrapped;
    }

    private byte[] Unwrap(byte[] wrapped)
    {
        if (wrapped.Length != WrappedHeaderLength + NonceLength + DataKeyLength + TagLength
            || wrapped[0] != Format)
        {
            throw Damaged("This data key was not wrapped in a layout this version understands.");
        }

        // Named before it is tried, because the two failures want different
        // answers from an operator: a key that says it is a different one is a
        // wrong `.env`, and a tag that does not verify is a damaged row.
        if (!CryptographicOperations.FixedTimeEquals(
                wrapped.AsSpan(1, MasterKey.IdLength), master.Id))
        {
            throw new Refusal(
                RefusalCode.MasterKeyMismatch,
                "This value was sealed under a different master key than the one this "
                + "instance was started with. Restoring a backup restores the key with it.");
        }

        var dataKey = new byte[DataKeyLength];

        try
        {
            using var aes = new AesGcm(master.Bytes, TagLength);
            aes.Decrypt(
                wrapped.AsSpan(WrappedHeaderLength, NonceLength),
                wrapped.AsSpan(WrappedHeaderLength + NonceLength, DataKeyLength),
                wrapped.AsSpan(WrappedHeaderLength + NonceLength + DataKeyLength),
                dataKey);
        }
        catch (AuthenticationTagMismatchException)
        {
            // The key says it is the right one and still does not open this. Two
            // keys sharing four bytes of identifier would land here, and so would
            // a wrapped key somebody edited; neither is something a caller did.
            throw Damaged(
                "This data key names the master key this instance holds and still does not "
                + "open under it. The row it is stored in has been changed since it was written.");
        }

        return dataKey;
    }

    /// <summary>
    /// The stored bytes are wrong, and the key is not why. It is a refusal rather
    /// than a <see cref="CryptographicException"/> so that the sentence reaches
    /// the operator who has to act on it instead of only this instance's log
    /// (VAULT-31): the answer to a damaged row is a restore, and nobody looks for
    /// one behind "Something went wrong on the server".
    /// </summary>
    private static Refusal Damaged(string detail) =>
        new(RefusalCode.SealedValueDamaged, detail);
}
