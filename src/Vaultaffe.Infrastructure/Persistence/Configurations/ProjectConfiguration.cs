using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Projects;
using Vaultaffe.Domain.References;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// An application or service (<c>docs/storage.md</c>). Its name appears in a
/// <c>vaultaffe://</c> reference, so the check constraint is ADR 0003's rule and
/// not a preference.
/// </summary>
public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("project", table => table.HasCheckConstraint(
            "ck_project_name", $"name ~ '{ReferenceName.Pattern}'"));

        builder.HasKey(p => p.Id).HasName("pk_project");

        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(ReferenceName.Limit)
            .IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");

        builder.Ignore(p => p.IsDeleted);

        // Unique across deleted rows too, and that is the point rather than an
        // oversight: a deleted project stays recoverable for 72 hours and its
        // name stays reserved for exactly as long, so recreating it cannot
        // silently replace its recoverable predecessor (Specification §6.5).
        // When the window ends the row is removed and the name frees itself.
        builder.HasIndex(p => new { p.OrganizationId, p.Name })
            .IsUnique()
            .HasDatabaseName("ux_project_organization_name");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_project_organization");
    }
}
