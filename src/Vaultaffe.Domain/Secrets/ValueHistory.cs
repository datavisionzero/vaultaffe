namespace Vaultaffe.Domain.Secrets;

/// <summary>
/// The bounds on what this product keeps, in one place, because Specification
/// §6.5 says the numbers are a decision rather than a placeholder — and that if
/// they turn out wrong they change in one line. This is that line.
/// </summary>
public static class ValueHistory
{
    /// <summary>
    /// How many superseded values a secret keeps. Every retained old value is
    /// usually a still-valid credential, which is the whole reason there is a
    /// bound at all. Vault defaults to ten, AWS guarantees one, Doppler keeps
    /// them forever.
    /// </summary>
    public const int Versions = 5;

    /// <summary>
    /// How long a superseded value is kept, whichever bound is reached first.
    /// The useful window for an undo is hours, not months.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(72);

    /// <summary>
    /// How long a deleted secret, environment or project stays recoverable.
    /// Repeating a deletion does not extend it, and deleting a container does
    /// not restart the clocks of what hangs underneath.
    /// </summary>
    public static readonly TimeSpan RecoveryWindow = TimeSpan.FromHours(72);
}
