using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.Domain.References;

/// <summary>
/// <c>vaultaffe://&lt;project&gt;/&lt;environment&gt;/&lt;KEY&gt;</c>.
///
/// Nothing in the MVP resolves one (Specification §5, §7). The scheme is settled
/// here anyway because it costs nothing today and will be as immutable as the
/// token format later. The organization is deliberately absent: it is implied by
/// the token that resolves the reference, which is why project names are unique
/// per organization rather than globally.
/// </summary>
public readonly record struct SecretReference(string Project, string Environment, string Key)
{
    /// <summary>The scheme, including its separator.</summary>
    public const string Scheme = "vaultaffe://";

    /// <summary>A reference to <paramref name="key"/>, or an exception.</summary>
    public static SecretReference To(string project, string environment, string key) =>
        new(
            ReferenceName.Require(project, nameof(project)),
            ReferenceName.Require(environment, nameof(environment)),
            SecretName.Require(key, nameof(key)));

    /// <summary>
    /// Reads a reference, or answers false. Nothing is escaped and nothing needs
    /// to be: the three parts cannot contain a '/' or the scheme separator,
    /// which is the whole reason ADR 0003 narrows them.
    /// </summary>
    public static bool TryParse(string? text, out SecretReference reference)
    {
        reference = default;

        if (text is null || !text.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = text[Scheme.Length..].Split('/');

        if (parts.Length != 3
            || !ReferenceName.IsValid(parts[0])
            || !ReferenceName.IsValid(parts[1])
            || !SecretName.IsValid(parts[2]))
        {
            return false;
        }

        reference = new SecretReference(parts[0], parts[1], parts[2]);

        return true;
    }

    public override string ToString() => $"{Scheme}{Project}/{Environment}/{Key}";
}
