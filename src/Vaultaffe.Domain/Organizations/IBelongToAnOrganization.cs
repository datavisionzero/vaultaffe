namespace Vaultaffe.Domain.Organizations;

/// <summary>
/// Every domain table carries the organization it belongs to (Specification §9).
/// Not because the parent could not be walked to — a secret's environment knows
/// its project and the project knows its organization — but because the filter
/// that keeps two organizations apart has to be one predicate over one column,
/// applied in one place. A join it had to walk would be a filter each query
/// could get wrong on its own.
/// </summary>
public interface IBelongToAnOrganization
{
    Guid OrganizationId { get; }
}
