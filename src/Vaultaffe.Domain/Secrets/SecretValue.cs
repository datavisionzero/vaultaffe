namespace Vaultaffe.Domain.Secrets;

/// <summary>
/// What may be written into a secret.
/// </summary>
/// <remarks>
/// Almost anything, and deliberately so: a PEM key with its newlines, a
/// connection string with its punctuation, whatever a vendor's command printed.
/// Specification §6.2 has the value arrive through stdin exactly so that nothing
/// has to be quoted or escaped on the way in, and a rule about its shape here
/// would put that back.
/// <para>
/// Two things it may not be. <b>Empty</b> — that is the placeholder, a state
/// meaning "a human still has to do something", and an empty string quietly
/// standing in for it is the bug the placeholder exists to prevent. And
/// <b>longer than the limit</b>, which is a bound on what one row may hold
/// rather than a statement about credentials.
/// </para>
/// <para>
/// <b>Nothing here trims.</b> Specification §6.2 strips exactly one trailing
/// newline, and it does so in the CLI, where the bytes came off a pipe and
/// <c>--raw</c> can say not to. By the time a value is in a request it is what
/// the caller meant, and a server that trimmed again would silently disagree
/// with <c>--raw</c>.
/// </para>
/// </remarks>
public static class SecretValue
{
    /// <summary>
    /// The longest a value may be. Large enough for a certificate chain, small
    /// enough that one row cannot be used as storage.
    /// </summary>
    public const int Limit = 64 * 1024;

    /// <summary>Whether <paramref name="value"/> is a value rather than a placeholder.</summary>
    public static bool IsValid(string? value) =>
        value is { Length: > 0 and <= Limit };
}
