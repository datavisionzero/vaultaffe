namespace Vaultaffe.Api.Http;

/// <summary>
/// Which version of the HTTP contract this build speaks, and where it lives.
/// </summary>
/// <remarks>
/// The API is versioned from the first release (Specification §6.3) and carries
/// its version as the second segment of every path
/// (<see href="../../../docs/adr/0005-the-api-carries-its-version-in-the-path.md">ADR 0005</see>).
/// A client reads <see cref="Supported"/> out of the handshake and picks; a
/// client that guesses and picks one this build does not have gets
/// <c>unsupported-api-version</c> rather than a route that happens not to exist.
/// <para>
/// One version today, and the list is a list anyway: the day a second one is
/// served the handshake has to name both, and that is the moment a single
/// constant would have been rewritten in five places.
/// </para>
/// </remarks>
public static class ApiVersion
{
    /// <summary>The version a client gets when it does not ask for one in particular.</summary>
    public const string Current = "v1";

    /// <summary>Where the endpoints of <see cref="Current"/> hang.</summary>
    public const string Route = "/api/" + Current;

    /// <summary>Every version this build serves, newest last.</summary>
    public static IReadOnlyList<string> Supported { get; } = [Current];

    /// <summary>Whether this build serves <paramref name="version"/>.</summary>
    public static bool Serves(string? version) =>
        version is not null && Supported.Contains(version, StringComparer.Ordinal);
}
