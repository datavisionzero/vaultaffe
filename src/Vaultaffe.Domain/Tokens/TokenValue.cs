using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Vaultaffe.Domain.Tokens;

/// <summary>
/// The value of a token, as it is handed to a person or a process
/// (Specification §5): a prefix naming its kind, and 256 bits of randomness
/// behind it. It exists once, at creation; what the instance keeps is
/// <see cref="Hash"/> and never this.
/// </summary>
/// <remarks>
/// The format is fixed here because it cannot be changed later — a token that is
/// already in a keychain, a CI variable and an agent's environment is a format
/// nobody can migrate (ADR 0004). The prefix is what makes a secret scanner able
/// to find one in a repository or a log, and what lets the server know which
/// kind it is talking to before it looks anything up.
/// <para>
/// <see cref="ToString"/> deliberately does not return the value.
/// Specification §6.5 makes a value in a log line a bug rather than an
/// untidiness, and a type whose interpolation prints a credential is how that
/// bug happens. The value leaves through <see cref="Reveal"/> and nowhere else.
/// </para>
/// </remarks>
public sealed class TokenValue
{
    /// <summary>
    /// How much randomness a token carries. 256 bits: a token is guessed or it
    /// is not, and there is no rate limit worth relying on instead.
    /// </summary>
    public const int RandomBytes = 32;

    /// <summary>
    /// How long the random part is once encoded — base64url without padding, so
    /// that a token survives a URL, a shell line and an environment variable
    /// without a single escaping rule.
    /// </summary>
    public const int RandomLength = 43;

    /// <summary>
    /// What every token value starts with, whatever its kind. One string a
    /// scanner, a reviewer or a grep can look for.
    /// </summary>
    public const string Prefix = "vaultaffe_";

    /// <summary>
    /// The whole format as a regular expression, for whoever teaches a secret
    /// scanner about this product. <c>TokenValueTests</c> holds it to what is
    /// actually issued.
    /// </summary>
    public static readonly string ScannerPattern =
        $@"{Prefix}(?:session|service|agent)_[A-Za-z0-9_-]{{{RandomLength}}}";

    private readonly string _value;

    private TokenValue(TokenKind kind, string value)
    {
        Kind = kind;
        _value = value;
    }

    /// <summary>Which kind this is — read from the prefix, not from a database.</summary>
    public TokenKind Kind { get; }

    /// <summary>
    /// The prefix of one kind. Whole words rather than initials: these end up in
    /// logs and error messages, where a person should be able to read what kind
    /// of credential leaked without consulting a table.
    /// </summary>
    public static string PrefixOf(TokenKind kind) => kind switch
    {
        TokenKind.Session => $"{Prefix}session_",
        TokenKind.Service => $"{Prefix}service_",
        TokenKind.Agent => $"{Prefix}agent_",
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "There are three token kinds, and each has a prefix of its own."),
    };

    /// <summary>A new token value of that kind. The only place one is created.</summary>
    public static TokenValue Issue(TokenKind kind) =>
        new(
            kind,
            PrefixOf(kind)
                + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes)));

    /// <summary>
    /// Read a value a caller presented. Exact: no trimming, no case folding, no
    /// tolerated padding — a token that arrives with a trailing newline out of a
    /// file or a shell is a different string, and pretending otherwise is how a
    /// format loosens itself over time.
    /// </summary>
    public static bool TryParse(string? presented, out TokenValue token)
    {
        token = null!;

        if (presented is null)
        {
            return false;
        }

        foreach (var kind in Enum.GetValues<TokenKind>())
        {
            var prefix = PrefixOf(kind);

            if (presented.Length != prefix.Length + RandomLength
                || !presented.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var random = presented.AsSpan(prefix.Length);

            if (!Base64Url.IsValid(random, out var decoded) || decoded != RandomBytes)
            {
                return false;
            }

            token = new TokenValue(kind, presented);
            return true;
        }

        return false;
    }

    /// <summary>
    /// What the instance stores and looks a token up by. Plain SHA-256 rather
    /// than a password hash: the input is 256 bits of uniform randomness, so
    /// there is no dictionary to make expensive, and authentication has to find
    /// the row by exactly this column.
    /// </summary>
    public byte[] Hash() => SHA256.HashData(Encoding.UTF8.GetBytes(_value));

    /// <summary>
    /// The value itself, to be shown exactly once and then forgotten. Named so
    /// that every place it happens reads as the deliberate act it is.
    /// </summary>
    public string Reveal() => _value;

    /// <summary>The kind, and nothing that could be pasted into a request.</summary>
    public override string ToString() => $"{PrefixOf(Kind)}…";
}
