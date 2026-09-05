namespace Vaultaffe.Domain.Identities;

/// <summary>
/// Where one device-code login has got to. The CLI polls and gets exactly one of
/// these; four of the five end the polling.
/// </summary>
public enum DeviceAuthorizationState
{
    /// <summary>Nobody has confirmed it yet. Keep polling.</summary>
    Pending = 1,

    /// <summary>A human confirmed it. The next poll receives the token.</summary>
    Approved = 2,

    /// <summary>A human refused it — they did not start this login.</summary>
    Denied = 3,

    /// <summary>Nobody confirmed it in time.</summary>
    Expired = 4,

    /// <summary>The token was already handed over. A device code works once.</summary>
    Redeemed = 5,
}
