namespace Vaultaffe.Application.Acts;

/// <summary>
/// How long a human stays signed in.
/// </summary>
/// <remarks>
/// Thirty days, and it is a decision rather than a placeholder. A session token
/// lives in the OS keychain (Specification §9) and stands in front of every
/// `vaultaffe` command a person runs, so a short life would mean a device-code
/// login every morning and the honest outcome of that is people reaching for a
/// service token instead — which is the credential that does not expire and does
/// not say who acted.
/// <para>
/// If it turns out wrong it changes on this line. Revocation is what makes a lost
/// laptop safe, not expiry.
/// </para>
/// </remarks>
public static class Sessions
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
}
