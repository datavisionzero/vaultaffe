namespace Vaultaffe.Domain.Identities;

/// <summary>Where an invitation has got to.</summary>
public enum InvitationState
{
    /// <summary>Handed out, still good, nobody has used it.</summary>
    Open = 1,

    /// <summary>Somebody signed up with it. An invitation works once.</summary>
    Accepted = 2,

    /// <summary>An administrator took it back before anybody used it.</summary>
    Withdrawn = 3,

    /// <summary>Nobody used it in time.</summary>
    Expired = 4,
}
