using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.Application.Ports;

/// <summary>
/// The instance's key material, and the only thing that turns a value into a
/// sealed one and back (Specification §6.3). A data key belongs to one secret
/// and never leaves this port bare; the master key it is wrapped under never
/// leaves the answer at all.
/// </summary>
/// <remarks>
/// The envelope is a port rather than a class the acts call directly, so that
/// what is wrapped under what is decided in one place (ADR 0002) — and so that
/// an instance which one day keeps its master key somewhere other than its own
/// configuration answers this differently and nothing else changes.
/// <para>
/// It speaks values and envelopes, never keys: the acts above it deal in "the
/// value of this secret", the store below it deals in the three columns, and the
/// key material stays inside whatever answers this.
/// </para>
/// </remarks>
public interface IKeyRing
{
    /// <summary>
    /// Seal a value. A secret that already has a data key hands its wrapped form
    /// in and keeps it — one data key per secret for its lifetime, which is what
    /// makes a later master-key rotation a rewrap of those rather than a
    /// re-encryption of every value. A secret writing its first value passes
    /// null and gets a fresh one.
    /// </summary>
    SealedValue Seal(string value, byte[]? wrappedDataKey);

    /// <summary>
    /// Open one. Throws rather than returning null when the value does not open:
    /// a ciphertext that fails its authentication tag is tampering or the wrong
    /// master key, and neither is a state a caller should be able to ignore.
    /// </summary>
    string Open(SealedValue value);
}
