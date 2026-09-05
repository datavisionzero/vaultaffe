namespace Vaultaffe.Api.Http;

/// <summary>
/// The refusals this API can make, one constant each.
/// </summary>
/// <remarks>
/// An error an agent has to be able to tell from another is its own code and
/// never a sentence: a client switches on <c>client-too-old</c>, it does not
/// match on the wording of a title we are free to improve. The code is the last
/// segment of the problem document's <c>type</c> and repeats as the <c>code</c>
/// member beside it (<c>docs/api.md</c>).
/// <para>
/// Only what something already refuses is in here. Authentication, authorization
/// and the refusals of the secrets surface arrive with the endpoints that make
/// them, because a code nothing raises is a promise to a client that nothing
/// keeps.
/// </para>
/// </remarks>
public enum ProblemCode
{
    /// <summary>Nothing by that name.</summary>
    NotFound = 1,

    /// <summary>The client named a contract version this build does not serve.</summary>
    UnsupportedApiVersion = 2,

    /// <summary>The client is a release older than this build accepts.</summary>
    ClientTooOld = 3,

    /// <summary>The client announced a version that is not a version.</summary>
    ClientVersionUnreadable = 4,

    /// <summary>Something went wrong on the server, and the document says nothing else.</summary>
    Internal = 5,
}
