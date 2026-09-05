using System.Security.Cryptography;

namespace Vaultaffe.Domain.Identities;

/// <summary>
/// The short code the CLI prints and a human types into a browser on some other
/// machine (Specification §6.2). Eight characters, shown as <c>XXXX-XXXX</c>.
/// </summary>
/// <remarks>
/// The alphabet is the twenty consonants of RFC 8628, and both halves of that
/// earn their place. No vowels means a code can never come out as a word, which
/// is the failure mode of every short random code a person is asked to read out.
/// No digits means the confusions that make people retype — <c>0</c> and
/// <c>O</c>, <c>1</c> and <c>I</c> or <c>l</c>, <c>5</c> and <c>S</c>, <c>2</c>
/// and <c>Z</c> — cannot arise at all, because only one half of each pair is in
/// the alphabet.
/// <para>
/// Eight characters is 20^8 — about 2.6 × 10^10 — and the code lives for ten
/// minutes. That is not a token's kind of unguessable and does not have to be:
/// what it protects is a request that still needs a human to sign in and press a
/// button. The device code, which is the credential, is
/// <see cref="DeviceCode"/> and is 256 bits.
/// </para>
/// </remarks>
public static class UserCode
{
    /// <summary>Consonants only: no word can come out of it, and no digit is in it.</summary>
    public const string Alphabet = "BCDFGHJKLMNPQRSTVWXZ";

    /// <summary>How many characters a code has, stored without its separator.</summary>
    public const int Length = 8;

    /// <summary>Where the dash goes when a person has to read it.</summary>
    public const int GroupLength = 4;

    /// <summary>A new code. The only place one is made.</summary>
    public static string Issue() =>
        string.Create(Length, 0, (code, _) =>
        {
            for (var character = 0; character < Length; character++)
            {
                code[character] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
        });

    /// <summary>
    /// The stored spelling of what a person typed. Case, spaces and dashes are
    /// all forgiven — they are how the code was shown, not what it is. Empty when
    /// what arrived is not a code at all.
    /// </summary>
    public static string Normalize(string? typed)
    {
        if (typed is null)
        {
            return string.Empty;
        }

        var code = new string([.. typed
            .Where(character => !char.IsWhiteSpace(character) && character is not '-')
            .Select(char.ToUpperInvariant)]);

        return code.Length == Length && code.All(Alphabet.Contains)
            ? code
            : string.Empty;
    }

    /// <summary>The code as a person is shown it: <c>XXXX-XXXX</c>.</summary>
    public static string ForReading(string code) =>
        code.Length == Length
            ? $"{code[..GroupLength]}-{code[GroupLength..]}"
            : code;
}
