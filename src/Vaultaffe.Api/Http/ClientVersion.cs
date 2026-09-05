using System.Globalization;

namespace Vaultaffe.Api.Http;

/// <summary>
/// The release a client announced, and the floor below which this instance stops
/// talking to one.
/// </summary>
/// <remarks>
/// Half of the version exchange of Specification §6.3. The other half is the
/// handshake: a client reads what this instance is and what it serves, and says
/// clearly that it is too old or too new. This is the safety net under that — a
/// client that skipped the handshake and is genuinely too old is told so in one
/// sentence rather than failing later at a field it did not expect.
/// <para>
/// Only the release triple is read. What follows a <c>-</c> or a <c>+</c> is a
/// pre-release or a build, and a contract floor is not the place to decide that
/// <c>0.4.0-rc.1</c> is not yet <c>0.4.0</c>. A leading <c>v</c> is accepted
/// because that is how the tag it came from is spelled.
/// </para>
/// </remarks>
public readonly record struct ClientVersion(int Major, int Minor, int Patch)
    : IComparable<ClientVersion>
{
    /// <summary>What a client announces itself in, on every request it makes.</summary>
    public const string Header = "Vaultaffe-Client";

    /// <summary>
    /// A working copy: the CLI's default when no tag named it
    /// (<c>src/cli/internal/version</c>). It is never refused for being old,
    /// because it is not a release and there is nothing to upgrade it to. Saying
    /// so is the CLI's job, and its <c>Released()</c> is where that is decided.
    /// </summary>
    public static readonly ClientVersion Unreleased = new(0, 0, 0);

    /// <summary>
    /// The oldest release this instance still serves. It is the first one there
    /// is, because nothing has been left behind yet; the day a contract change
    /// leaves a release behind, this line moves and that is the whole of it.
    /// </summary>
    public static readonly ClientVersion Minimum = new(0, 1, 0);

    /// <summary>
    /// Read what a client announced. False for anything that is not three
    /// numbers, including an empty header — a client that says something has
    /// said something, and guessing what would be the field this exchange exists
    /// to avoid failing at.
    /// </summary>
    public static bool TryParse(string? presented, out ClientVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }

        var release = presented.Trim();

        if (release.StartsWith('v'))
        {
            release = release[1..];
        }

        foreach (var separator in new[] { '-', '+' })
        {
            var at = release.IndexOf(separator);

            if (at >= 0)
            {
                release = release[..at];
            }
        }

        var parts = release.Split('.');

        if (parts.Length != 3)
        {
            return false;
        }

        var numbers = new int[3];

        for (var part = 0; part < 3; part++)
        {
            if (!int.TryParse(parts[part], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[part]))
            {
                return false;
            }
        }

        version = new ClientVersion(numbers[0], numbers[1], numbers[2]);

        return true;
    }

    public static bool operator <(ClientVersion left, ClientVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ClientVersion left, ClientVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ClientVersion left, ClientVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ClientVersion left, ClientVersion right) => left.CompareTo(right) >= 0;

    public int CompareTo(ClientVersion other)
    {
        var major = Major.CompareTo(other.Major);
        var minor = Minor.CompareTo(other.Minor);

        return major != 0 ? major : minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    /// <summary>Whether this instance still serves a client of this release.</summary>
    public bool IsServed => this == Unreleased || this >= Minimum;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}
