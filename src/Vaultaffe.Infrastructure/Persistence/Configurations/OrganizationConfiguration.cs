using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The tenancy boundary (<c>docs/storage.md</c>). The MVP holds exactly one row,
/// and the schema is indifferent to that: several organizations later are a
/// question of the interface, not of a migration (Specification §9).
/// </summary>
public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organization");

        builder.HasKey(o => o.Id).HasName("pk_organization");

        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.Name)
            .HasColumnName("name")
            .HasMaxLength(Organization.NameLimit)
            .IsRequired();
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
