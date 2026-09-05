namespace Vaultaffe.Domain.Identities;

/// <summary>
/// What this instance requires of a password before anything hashes it.
/// </summary>
/// <remarks>
/// One rule, and it is length. Composition rules — a digit, a symbol, a capital
/// — are the ones every guidance since NIST SP 800-63B has told products to drop:
/// they push people towards `Password1!` and they are why the same password ends
/// up in three places. Length is the one that buys anything.
/// <para>
/// The number is here rather than in whatever hashes it, because it is a rule of
/// the product and not a property of Argon2id. Where it is enforced is every path
/// that sets one: the first run, and a reset an administrator performs.
/// </para>
/// </remarks>
public static class Password
{
    /// <summary>The shortest password this instance accepts.</summary>
    public const int MinimumLength = 12;

    /// <summary>
    /// The longest. Not a security bound — it is what stops a request body from
    /// turning into a minute of Argon2id.
    /// </summary>
    public const int MaximumLength = 1024;

    public static bool IsValid(string? password) =>
        password is not null && password.Length is >= MinimumLength and <= MaximumLength;

    /// <summary>The password, or an exception naming the rule it broke.</summary>
    public static string Require(string? password, string parameterName) =>
        IsValid(password)
            ? password!
            : throw new ArgumentException(
                $"A password is between {MinimumLength} and {MaximumLength} characters.",
                parameterName);
}
