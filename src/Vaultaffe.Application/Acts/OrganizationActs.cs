using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Refusals;

namespace Vaultaffe.Application.Acts;

/// <summary>The organization, as a client reads it.</summary>
public sealed record OrganizationRow(Guid Id, string Name, DateTimeOffset CreatedAt);

/// <summary>
/// The organization the caller is in (Specification §5). Exactly one in the MVP,
/// called <c>Default</c> until somebody renames it.
/// </summary>
/// <remarks>
/// Reading it is nobody's privilege: a token knows which organization it is in
/// the moment it authenticates, and the name is what a screen puts in its
/// header. Renaming is an administrator's, because who the organization is, is a
/// decision about people (§6.4).
/// </remarks>
public sealed class ReadOrganization(IIdentityStore identities, ICallerIdentity caller)
{
    public async Task<OrganizationRow> ExecuteAsync(CancellationToken cancellationToken)
    {
        _ = caller.Required;

        var organization = await identities.FindOrganizationAsync(cancellationToken)
            ?? throw Refusal.NotFound("This instance has not been started.");

        return Row(organization);
    }

    internal static OrganizationRow Row(Organization organization) =>
        new(organization.Id, organization.Name, organization.CreatedAt);
}

/// <summary>Renaming it. The name is editable, and that is the whole of it (§6.1).</summary>
public sealed class RenameOrganization(IIdentityStore identities, ICallerIdentity caller)
{
    public async Task<OrganizationRow> ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        _ = caller.Required;

        var trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length is 0 or > Organization.NameLimit)
        {
            throw Refusal.Validation(
                "name",
                $"An organization name is between 1 and {Organization.NameLimit} characters.");
        }

        var organization = await identities.FindOrganizationAsync(cancellationToken)
            ?? throw Refusal.NotFound("This instance has not been started.");

        organization.RenameTo(trimmed);

        await identities.SaveAsync(cancellationToken);

        return ReadOrganization.Row(organization);
    }
}
