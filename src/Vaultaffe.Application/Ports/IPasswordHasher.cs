namespace Vaultaffe.Application.Ports;

/// <summary>
/// Turns a password into something an instance can keep, and checks one against
/// it. A port, because which algorithm and which cost is an operational decision
/// that should be changeable without any act knowing (Specification §9).
/// </summary>
/// <remarks>
/// The encoded value is self-describing: it carries the algorithm and its
/// parameters, so raising the cost later re-hashes on the next sign-in rather
/// than migrating a column.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Hash a password. The result is what goes in the column.</summary>
    Task<string> HashAsync(string password, CancellationToken cancellationToken);

    /// <summary>
    /// Whether <paramref name="password"/> is the one behind
    /// <paramref name="encoded"/>. False rather than an exception for anything
    /// unreadable: a hash this build cannot parse is a failed sign-in, not a
    /// crashed request.
    /// </summary>
    Task<bool> VerifyAsync(string encoded, string password, CancellationToken cancellationToken);

    /// <summary>
    /// An encoded hash of nothing, at this hasher's own cost. A sign-in for an
    /// address no user has verifies against it, so that an unknown address and a
    /// wrong password take the same time — otherwise the form answers a question
    /// it should not: whether that address belongs to somebody here.
    /// </summary>
    string DecoyHash { get; }
}
