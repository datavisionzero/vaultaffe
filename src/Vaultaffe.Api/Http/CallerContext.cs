using Vaultaffe.Application.Ports;

namespace Vaultaffe.Api.Http;

/// <summary>
/// Who authenticated this request, for the length of this request.
/// </summary>
/// <remarks>
/// It answers two ports at once, and that is the point:
/// <see cref="ICallerIdentity"/> is what the acts ask, and
/// <see cref="IOrganizationScope"/> is what the one query filter of Specification
/// §9 reads. A caller can therefore never be inside a different organization from
/// the one their queries are filtered by — there is one answer and both questions
/// reach it.
/// <para>
/// Before the token middleware has run, and on every request that carries no
/// token, it answers nobody. Nobody is inside no organization, and the filter
/// compares a null against a column and matches nothing — which is the safe end
/// of that comparison.
/// </para>
/// </remarks>
internal sealed class CallerContext : ICallerIdentity, IOrganizationScope
{
    public Caller? Caller { get; private set; }

    public Guid? OrganizationId => Caller?.OrganizationId;

    /// <summary>Set once, by the middleware that authenticated the token.</summary>
    public void Authenticated(Caller caller) => Caller = caller;
}
