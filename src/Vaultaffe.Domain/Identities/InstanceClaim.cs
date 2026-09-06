using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Vaultaffe.Domain.Identities;

/// <summary>
/// The claim secret: what the first run has to present, so that an instance
/// nobody has started yet is not owned by whoever reaches its port first
/// (ADR 0019).
/// </summary>
/// <remarks>
/// <para>
/// <b>The instance makes it, not the operator.</b> That is the whole difference
/// from the bootstrap secret ADR 0007 turned down: there is nothing to put in a
/// <c>.env</c>, nothing to generate, and nothing to skip by copying an example
/// file. The operator reads it out of the instance's own log and pastes it into
/// the first run.
/// </para>
/// <para>
/// <b>It is stored as it is read, and that is deliberate.</b> Every other
/// credential in this product is kept as a hash, because the instance never has
/// to say it again. This one it does: it is printed at every start for as long
/// as the instance is unclaimed, so that losing a terminal is never final —
/// restarting the container prints the same secret rather than a new one. A hash
/// would force a fresh secret at every start, and then the log history would be
/// full of values that no longer work, which is a worse trap than the row this
/// keeps. What it guards is an <b>empty</b> instance, and it is deleted the
/// moment there is anything in it to guard.
/// </para>
/// </remarks>
public sealed class InstanceClaim
{
    /// <summary>
    /// How much randomness it carries. The same 256 bits as a token, for the
    /// same reason: it is guessed or it is not, and no rate limit is worth
    /// relying on instead — least of all one that could lock an operator out of
    /// their own unstarted instance.
    /// </summary>
    public const int RandomBytes = 32;

    /// <summary>
    /// What it starts with. A prefix of its own rather than a token's, because
    /// this is not a token: it authenticates nobody, carries no scopes and opens
    /// exactly one endpoint. A scanner that finds one in a paste knows which of
    /// the two it is looking at.
    /// </summary>
    public const string Prefix = "vaultaffe_claim_";

    public InstanceClaim(Guid id, string secret, DateTimeOffset createdAt)
    {
        Id = id;
        Secret = secret;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The secret itself. Named plainly rather than hidden behind a
    /// <c>Reveal()</c>: unlike a token value this one is meant to be read back
    /// and printed, and a method pretending otherwise would suggest a protection
    /// that is not there.
    /// </summary>
    public string Secret { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A new one. The only place a claim secret is created.</summary>
    public static InstanceClaim Issue(Guid id, DateTimeOffset now) =>
        new(id, Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes)), now);

    /// <summary>
    /// Whether what a caller presented is this secret. Constant time, so that
    /// the endpoint cannot be asked how much of a guess was right.
    /// </summary>
    public bool Matches(string? presented) =>
        presented is not null
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(Secret));

    /// <summary>
    /// Not the secret. This type is interpolated into log lines by the one place
    /// that prints it deliberately, and nowhere else should be able to do it by
    /// accident (Specification §6.5).
    /// </summary>
    public override string ToString() => $"{Prefix}…";
}
