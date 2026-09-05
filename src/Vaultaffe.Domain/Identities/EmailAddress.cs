using System.Text.RegularExpressions;

namespace Vaultaffe.Domain.Identities;

/// <summary>
/// How a person is addressed when they sign in. Deliberately a loose rule: this
/// instance sends no mail (Specification §6.1), so an address here is an
/// identifier a human types, not a delivery target — and a strict rule would
/// only refuse addresses that are perfectly valid.
/// </summary>
/// <remarks>
/// What is enforced is what a unique index needs: one <c>@</c>, something on
/// either side of it, no whitespace, and a single lower-case spelling. Case is
/// folded because <c>Alex@example.com</c> and <c>alex@example.com</c> are the
/// same person to everyone except a database index, and a login that depends on
/// how somebody's keyboard felt that morning is not a login.
/// </remarks>
public static partial class EmailAddress
{
    /// <summary>The longest address this accepts — the limit RFC 5321 puts on a path.</summary>
    public const int Limit = 254;

    /// <summary>
    /// The rule, unanchored, so that Postgres and .NET can each add the anchors
    /// they mean (<see cref="Pattern"/> and <see cref="Rule"/>).
    /// </summary>
    public const string Body = @"[^@\s]+@[^@\s]+";

    /// <summary>The rule as the database check constraint states it.</summary>
    public const string Pattern = $"^{Body}$";

    /// <summary>The one spelling this address is stored and looked up under.</summary>
    public static string Normalize(string? address) =>
        (address ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Whether <paramref name="address"/> is one this instance can store.</summary>
    public static bool IsValid(string? address)
    {
        var normalized = Normalize(address);

        return normalized.Length is > 0 and <= Limit && Rule().IsMatch(normalized);
    }

    /// <summary>The normalized address, or an exception naming the rule it broke.</summary>
    public static string Require(string? address, string parameterName)
    {
        if (!IsValid(address))
        {
            throw new ArgumentException(
                $"'{address}' is not an email address. One '@', something on either side of it, "
                + $"no spaces, at most {Limit} characters.",
                parameterName);
        }

        return Normalize(address);
    }

    // \A and \z rather than ^ and $, so that this refuses exactly what the check
    // constraint refuses: Postgres's '$' matches the end of the string and
    // nothing else, while .NET's also matches before a trailing newline. A rule
    // enforced in two places has to mean one thing in both.
    [GeneratedRegex($@"\A(?:{Body})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Rule();
}
