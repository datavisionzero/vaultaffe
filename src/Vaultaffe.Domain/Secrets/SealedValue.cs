namespace Vaultaffe.Domain.Secrets;

/// <summary>
/// A value at rest (Specification §6.3): the ciphertext, the nonce it was sealed
/// with, and the data key it was sealed under — wrapped by the instance master
/// key, never bare. These are the three columns a secret carries, and the two a
/// superseded version carries.
/// </summary>
/// <remarks>
/// The rule this type holds is the one the database also holds in
/// <c>ck_secret_value_sealed_whole</c>: a value is sealed whole or not at all. A
/// ciphertext without the nonce and data key it was sealed under is a value
/// nobody can ever open again, and it should not be constructible here either.
/// <para>
/// What the bytes mean is deliberately not decided here. Domain says a value
/// never rests unsealed; which algorithm sealed it, and how these arrays are
/// laid out inside, is Infrastructure's (ADR 0004).
/// </para>
/// </remarks>
public sealed class SealedValue
{
    public SealedValue(byte[] wrappedDataKey, byte[] nonce, byte[] ciphertext)
    {
        WrappedDataKey = NotEmpty(wrappedDataKey, nameof(wrappedDataKey));
        Nonce = NotEmpty(nonce, nameof(nonce));
        Ciphertext = NotEmpty(ciphertext, nameof(ciphertext));
    }

    /// <summary>
    /// The secret's data key, wrapped by the instance master key. One per
    /// secret, kept for its lifetime — which is what later makes rotating the
    /// master key a rewrap of these rather than a re-encryption of every value.
    /// </summary>
    public byte[] WrappedDataKey { get; }

    /// <summary>The nonce this value was sealed with. A fresh one per write.</summary>
    public byte[] Nonce { get; }

    /// <summary>The value itself, sealed. Never anything else.</summary>
    public byte[] Ciphertext { get; }

    private static byte[] NotEmpty(byte[] part, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(part, parameterName);

        return part.Length > 0
            ? part
            : throw new ArgumentException(
                "A sealed value is sealed whole or not at all; an empty part would leave a "
                + "value nobody can open again.",
                parameterName);
    }
}
