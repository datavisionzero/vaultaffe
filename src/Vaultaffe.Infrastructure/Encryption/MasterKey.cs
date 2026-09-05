using System.Security.Cryptography;

namespace Vaultaffe.Infrastructure.Encryption;

/// <summary>
/// The one key an operator holds: 256 bits from the instance configuration, under
/// which every secret's data key is wrapped (Specification §6.3). Losing it means
/// losing every value in the instance, which is why the backup path documents it
/// beside the database dump rather than after it.
/// </summary>
/// <remarks>
/// The key carries an <see cref="Id"/> derived from itself rather than a version
/// number somebody configures. Every wrapped data key names the key it is under,
/// so an instance started with the wrong key says so instead of failing as if the
/// data were corrupt — and the rotation that is not an MVP feature (§7) can later
/// tell a rewrapped row from one still waiting, with nothing to keep in sync.
/// </remarks>
public sealed class MasterKey
{
    /// <summary>AES-256: the key is 32 bytes and nothing else is accepted.</summary>
    public const int Length = 32;

    /// <summary>How much of the derived identifier is kept. Enough to tell keys apart.</summary>
    public const int IdLength = 4;

    private readonly byte[] _key;

    public MasterKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != Length)
        {
            throw new ArgumentException(
                $"A master key is {Length} bytes. This one is {key.Length}.", nameof(key));
        }

        _key = key;
        Id = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: key,
            outputLength: IdLength,
            salt: null,
            info: "vaultaffe master key id"u8.ToArray());
    }

    /// <summary>
    /// Which key this is, derived from the key itself. It is written into every
    /// wrapped data key, and it is not a secret: four bytes of an HKDF output
    /// over 256 bits of randomness say which key was used and nothing about it.
    /// </summary>
    public byte[] Id { get; }

    internal ReadOnlySpan<byte> Bytes => _key;

    /// <summary>
    /// The key an installation configured, or an exception that says how to make
    /// one. An instance without a usable master key does not start: the
    /// alternative is an instance that runs, accepts values and cannot open them
    /// after the next restart.
    /// </summary>
    public static MasterKey FromConfiguredValue(string? configured, string configurationName)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"No master key. An installation sets {configurationName} to {Length} random "
                + "bytes in base64 — `openssl rand -base64 32` — and keeps it with the backup: "
                + "a database dump without it is worthless.");
        }

        Span<byte> key = stackalloc byte[Length];

        if (!Convert.TryFromBase64String(configured, key, out var written) || written != Length)
        {
            throw new InvalidOperationException(
                $"The master key in {configurationName} is not {Length} bytes in base64. "
                + "`openssl rand -base64 32` makes one.");
        }

        return new MasterKey(key.ToArray());
    }
}
