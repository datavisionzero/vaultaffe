using System.Reflection;

namespace Vaultaffe.Api.Hosting;

/// <summary>
/// Which release this instance is, as one string, read once.
/// </summary>
/// <remarks>
/// The tag is the only source of a version (Directory.Build.props): a release
/// build passes <c>-p:Version=</c> from the tag it was started by, and the Go
/// half of the same release takes the same string through its linker flag. A
/// build nobody tagged is <c>0.0.0-dev</c> and says so rather than claiming a
/// number that was never released.
/// <para>
/// This is not the API version. The contract's version is
/// <see cref="Http.ApiVersion"/> and moves when the contract does; this one
/// moves with every release, and the two are answered side by side in the
/// handshake so that a client can tell "your server is old" from "your server
/// does not speak this contract".
/// </para>
/// </remarks>
public static class InstanceVersion
{
    /// <summary>What every response carries it in, refused and failed ones included.</summary>
    public const string Header = "Vaultaffe-Version";

    /// <summary>What a build that was never tagged calls itself.</summary>
    public const string Untagged = "0.0.0-dev";

    /// <summary>The release this instance was cut from.</summary>
    public static string Value { get; } = Read();

    private static string Read()
    {
        var informational = typeof(InstanceVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return Untagged;
        }

        // SourceLink appends "+<commit>" to the informational version. That is
        // the build, not the release, and a client comparing versions should not
        // have to know the difference.
        var build = informational.IndexOf('+');

        return build < 0 ? informational : informational[..build];
    }
}
