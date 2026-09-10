using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.Identities;

/// <summary>
/// One project, or one environment of it, that a person has agreed an enrollment
/// may reach — recorded while there is still no token to hang it on.
/// </summary>
/// <remarks>
/// It is not a <see cref="Tokens.TokenBinding"/>, and the reason is the order the
/// flow happens in: a token's value exists exactly once, in the answer that
/// creates it, so the token cannot be created until the machine that asked comes
/// back to collect it. Between the confirmation and that collection there is a
/// reach a person has agreed to and nothing to attach it to — and a
/// <c>token_binding</c> row pointing at no token is a row its own foreign key
/// could not hold.
/// <para>
/// These rows go when the enrollment does. They are a decision in flight and
/// never a record of anything: what the token ended up reaching is in
/// <c>token_binding</c>, written when it was finally issued.
/// </para>
/// </remarks>
public sealed class EnrollmentBinding : IBelongToAnOrganization
{
    public EnrollmentBinding(
        Guid id,
        Guid organizationId,
        Guid authorizationId,
        Guid projectId,
        Guid? environmentId)
    {
        Id = id;
        OrganizationId = organizationId;
        AuthorizationId = authorizationId;
        ProjectId = projectId;
        EnvironmentId = environmentId;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The enrollment this was agreed for.</summary>
    public Guid AuthorizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>One environment of the project, or null for all of them.</summary>
    public Guid? EnvironmentId { get; private set; }
}
