using System.Text.RegularExpressions;

namespace Vaultaffe.Domain.References;

/// <summary>
/// The rule for a name that appears inside a reference — a project's and an
/// environment's (ADR 0003). Specification §5 fixes
/// <c>vaultaffe://&lt;project&gt;/&lt;environment&gt;/&lt;KEY&gt;</c> now, even
/// though nothing in the MVP resolves it, and says the two names therefore stay
/// narrow: no spaces.
/// </summary>
public static partial class ReferenceName
{
    /// <summary>The longest such a name may be.</summary>
    public const int Limit = 64;

    /// <summary>
    /// Lower-case letters, digits, and separators between them — unanchored,
    /// because the two places that apply it do not spell their anchors the same
    /// way. Postgres reads <see cref="Pattern"/>; .NET reads <see cref="Rule"/>.
    /// </summary>
    public const string Body = "[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?";

    /// <summary>
    /// The rule as the database check constraint states it. In Postgres '$'
    /// matches the end of the string and nothing else.
    /// </summary>
    public const string Pattern = $"^{Body}$";

    /// <summary>
    /// The rule in a sentence, for whoever broke it. One wording, because the
    /// entity constructors and the acts both say it and a person reading two
    /// versions of one rule reasonably wonders whether there are two.
    /// </summary>
    public static string Explanation { get; } =
        $"Use lower-case letters, digits, '.', '-' and '_', at most {Limit} characters, "
        + "starting and ending with a letter or digit.";

    /// <summary>Whether <paramref name="name"/> may appear in a reference.</summary>
    public static bool IsValid(string? name) =>
        name is not null && name.Length <= Limit && Rule().IsMatch(name);

    /// <summary>
    /// The name, or an exception naming the rule it broke. The caller passes the
    /// parameter it read the name from so the message says where it came from.
    /// </summary>
    public static string Require(string? name, string parameterName)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException(
                $"'{name}' is not usable in a vaultaffe:// reference. {Explanation}",
                parameterName);
        }

        return name!;
    }

    // \A and \z rather than ^ and $, for the reason in SecretName: .NET's '$'
    // would let a trailing newline through.
    [GeneratedRegex($@"\A(?:{Body})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Rule();
}
