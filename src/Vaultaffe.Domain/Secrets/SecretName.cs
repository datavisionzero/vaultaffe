using System.Text.RegularExpressions;

namespace Vaultaffe.Domain.Secrets;

/// <summary>
/// <c>^[A-Z_][A-Z0-9_]*$</c> — the rule Specification §5 states, and the same one
/// Doppler uses: secrets end up as environment variables, and this is the shape
/// that spares everyone the special-character discussion.
/// </summary>
public static partial class SecretName
{
    /// <summary>The longest a secret name may be.</summary>
    public const int Limit = 128;

    /// <summary>
    /// The rule, spelled as the specification spells it — and unanchored,
    /// because the two places that apply it do not spell their anchors the same
    /// way. Postgres reads <see cref="Pattern"/>; .NET reads <see cref="Rule"/>.
    /// </summary>
    public const string Body = "[A-Z_][A-Z0-9_]*";

    /// <summary>
    /// The rule as the database check constraint states it. In Postgres '$'
    /// matches the end of the string and nothing else.
    /// </summary>
    public const string Pattern = $"^{Body}$";

    /// <summary>Whether <paramref name="name"/> is a usable secret name.</summary>
    public static bool IsValid(string? name) =>
        name is not null && name.Length <= Limit && Rule().IsMatch(name);

    /// <summary>The name, or an exception naming the rule it broke.</summary>
    public static string Require(string? name, string parameterName)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a secret name. Use upper-case letters, digits and "
                + $"'_', at most {Limit} characters, not starting with a digit.",
                parameterName);
        }

        return name!;
    }

    // \A and \z rather than ^ and $: in .NET '$' also matches immediately
    // before a trailing newline, so `DATABASE_URL\n` would pass a rule that
    // reads as if it could not. A key with an invisible newline on the end is
    // exactly the bug Specification §6.2 spends a paragraph on.
    [GeneratedRegex($@"\A(?:{Body})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Rule();
}
