namespace Vaultaffe.Domain.Refusals;

/// <summary>
/// Every way this product says no, one constant each.
/// </summary>
/// <remarks>
/// A refusal an agent has to be able to tell from another is its own code and
/// never a sentence: a client switches on <c>client-too-old</c>, it does not
/// match on the wording of a title we are free to improve. What each code means
/// on the wire — its status, its title, the shape of the document it arrives in —
/// is the API's business and lives there (<c>docs/api.md</c>); the list of them
/// is here, because a refusal is a rule saying no and the rules are Domain's.
/// <para>
/// Only what something already refuses is in here. A code nothing raises is a
/// promise to a client that nothing keeps, so each one arrives with the act or
/// the endpoint that makes it.
/// </para>
/// </remarks>
public enum RefusalCode
{
    /// <summary>A field is missing, malformed or over its limit.</summary>
    Validation = 1,

    /// <summary>Nothing by that name.</summary>
    NotFound = 2,

    /// <summary>No token, an unknown one, a revoked one — or the wrong password.</summary>
    Unauthenticated = 3,

    /// <summary>The caller is authenticated and still may not do this.</summary>
    Forbidden = 4,

    /// <summary>The instance already has its first user; there is no second first run.</summary>
    AlreadyStarted = 5,

    /// <summary>Nobody has confirmed this login yet. Keep polling.</summary>
    DevicePending = 6,

    /// <summary>A human said they did not start this login.</summary>
    DeviceDenied = 7,

    /// <summary>Nobody confirmed this login in time, or its token was already collected.</summary>
    DeviceExpired = 8,

    /// <summary>The client named a version of the contract this build does not serve.</summary>
    UnsupportedApiVersion = 9,

    /// <summary>The client is a release older than this build accepts.</summary>
    ClientTooOld = 10,

    /// <summary>The client announced a version that is not a version.</summary>
    ClientVersionUnreadable = 11,

    /// <summary>Something went wrong on the server, and the document says nothing else.</summary>
    Internal = 12,

    /// <summary>One of the short list only a person may do (Specification §6.4).</summary>
    HumanOnly = 13,

    /// <summary>The token is missing a scope this needs.</summary>
    InsufficientScope = 14,

    /// <summary>The token is bound elsewhere and does not reach that project or environment.</summary>
    OutOfReach = 15,
}
