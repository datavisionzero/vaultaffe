namespace Vaultaffe.Domain.Organizations;

/// <summary>
/// The tenancy boundary (Specification §5). Every record belongs to exactly one,
/// and organizations never see each other. The MVP creates exactly one — the
/// default organization of the first run — which is why the interface has no way
/// to make a second, and the data model has every way.
/// </summary>
public sealed class Organization
{
    public Organization(Guid id, string name, DateTimeOffset createdAt)
    {
        Id = id;
        Name = Named(name);
        CreatedAt = createdAt;
    }

    /// <summary>The longest an organization name may be.</summary>
    public const int NameLimit = 100;

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // Deliberately not IBelongToAnOrganization: this table *is* the
    // organization, and a row of it cannot carry a foreign key to itself that a
    // filter could compare. The context filters it by its own key instead, which
    // is the one place that difference is written down.

    /// <summary>Rename. The organization name is editable (Specification §6.1).</summary>
    public void RenameTo(string name) => Name = Named(name);

    // Deliberately not a ReferenceName: an organization never appears in a
    // vaultaffe:// reference (Specification §5), so nothing is gained by
    // narrowing what a team may call itself.
    private static string Named(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();

        return trimmed.Length switch
        {
            0 => throw new ArgumentException("An organization needs a name.", nameof(name)),
            > NameLimit => throw new ArgumentException(
                $"An organization name is at most {NameLimit} characters.", nameof(name)),
            _ => trimmed,
        };
    }
}
