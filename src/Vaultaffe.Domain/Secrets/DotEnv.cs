using System.Text;

namespace Vaultaffe.Domain.Secrets;

/// <summary>
/// The <c>.env</c> format, read and written (Specification §6.2).
/// </summary>
/// <remarks>
/// Migration is a success criterion (§11), and the way in is a file somebody
/// already has. There is no standard for this format — every tool's parser
/// differs at the edges — so what this one does is written down rather than
/// inferred, and it is the same rules in both directions.
/// <para>
/// <b>Reading.</b> Blank lines and lines whose first non-blank character is
/// <c>#</c> are skipped. A leading <c>export</c> is allowed, because that is what
/// half the files in the world have. Everything before the first <c>=</c> is the
/// key. A value in single quotes is literal; a value in double quotes may span
/// lines and understands <c>\n</c>, <c>\r</c>, <c>\t</c>, <c>\\</c> and
/// <c>\"</c>; anything else is taken as written with surrounding blanks removed.
/// An unquoted value keeps its <c>#</c>: a trailing comment cannot be told from a
/// password containing one, and guessing wrong loses a character of a credential.
/// </para>
/// <para>
/// <b>Writing.</b> Always double-quoted and escaped, whatever the value looks
/// like. A format that quotes only when it has to is a format that is one unusual
/// value away from producing a file it cannot read back.
/// </para>
/// </remarks>
public static class DotEnv
{
    /// <summary>One line of a file: a key, and what it was set to.</summary>
    public readonly record struct Setting(string Key, string Value);

    /// <summary>
    /// What a file could not be read as: the line, and what was wrong with it.
    /// Reported rather than thrown, so that an import can say which lines it
    /// could not use instead of refusing a file for one of them.
    /// </summary>
    public readonly record struct Complaint(int Line, string Reason);

    /// <summary>The settings in <paramref name="content"/>, and what it could not read.</summary>
    public static (IReadOnlyList<Setting> Settings, IReadOnlyList<Complaint> Complaints) Read(
        string? content)
    {
        var settings = new List<Setting>();
        var complaints = new List<Complaint>();

        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (line.Length is 0 || line[0] is '#')
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                complaints.Add(new Complaint(index + 1, "No '=' on this line."));
                continue;
            }

            var key = line[..separator].Trim();
            var rest = line[(separator + 1)..];

            if (!SecretName.IsValid(key))
            {
                complaints.Add(new Complaint(index + 1, $"'{key}' is not a secret name."));
                continue;
            }

            // A double-quoted value may run past the end of this line, which is
            // how a PEM key survives a .env file at all.
            if (rest.StartsWith('"'))
            {
                var closing = ClosingQuote(lines, ref index, rest);

                if (closing is null)
                {
                    complaints.Add(new Complaint(index + 1, "A double quote is never closed."));
                    continue;
                }

                settings.Add(new Setting(key, Unescape(closing)));
                continue;
            }

            if (rest.StartsWith('\'') && rest.Length > 1 && rest.EndsWith('\''))
            {
                settings.Add(new Setting(key, rest[1..^1]));
                continue;
            }

            settings.Add(new Setting(key, rest.Trim()));
        }

        return (settings, complaints);
    }

    /// <summary>
    /// <paramref name="settings"/> as a file, in the order they were given. Every
    /// value is quoted and escaped, so that what this writes is something this
    /// can read.
    /// </summary>
    public static string Write(IEnumerable<Setting> settings)
    {
        var file = new StringBuilder();

        foreach (var (key, value) in settings)
        {
            file.Append(key).Append("=\"").Append(Escape(value)).Append("\"\n");
        }

        return file.ToString();
    }

    /// <summary>The body of a double-quoted value, gathering lines until it closes.</summary>
    private static string? ClosingQuote(string[] lines, ref int index, string first)
    {
        var body = new StringBuilder();
        var rest = first[1..];

        while (true)
        {
            var end = Unescaped(rest, '"');

            if (end >= 0)
            {
                return body.Append(rest[..end]).ToString();
            }

            body.Append(rest).Append('\n');

            if (++index >= lines.Length)
            {
                return null;
            }

            rest = lines[index];
        }
    }

    /// <summary>The first <paramref name="what"/> that is not itself escaped.</summary>
    private static int Unescaped(string text, char what)
    {
        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] is '\\')
            {
                at++;
                continue;
            }

            if (text[at] == what)
            {
                return at;
            }
        }

        return -1;
    }

    private static string Unescape(string text)
    {
        var value = new StringBuilder(text.Length);

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] is not '\\' || at + 1 >= text.Length)
            {
                value.Append(text[at]);
                continue;
            }

            switch (text[++at])
            {
                case 'n': value.Append('\n'); break;
                case 'r': value.Append('\r'); break;
                case 't': value.Append('\t'); break;
                case '\\': value.Append('\\'); break;
                case '"': value.Append('"'); break;

                // An escape this format does not know keeps both characters,
                // because a backslash inside a password is a backslash.
                default: value.Append('\\').Append(text[at]); break;
            }
        }

        return value.ToString();
    }

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
}
