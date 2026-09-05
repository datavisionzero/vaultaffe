using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Projects;
using Vaultaffe.Domain.References;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// One environment of one project (<c>docs/storage.md</c>) — and only one level,
/// because there is no branch config and no personal config below it
/// (Specification §5).
/// </summary>
public sealed class EnvironmentConfiguration : IEntityTypeConfiguration<Environment>
{
    public void Configure(EntityTypeBuilder<Environment> builder)
    {
        builder.ToTable("environment", table => table.HasCheckConstraint(
            "ck_environment_name", $"name ~ '{ReferenceName.Pattern}'"));

        builder.HasKey(e => e.Id).HasName("pk_environment");

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(ReferenceName.Limit)
            .IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        builder.Ignore(e => e.IsDeleted);

        // Unique including deleted rows, for the reason in ProjectConfiguration.
        builder.HasIndex(e => new { e.ProjectId, e.Name })
            .IsUnique()
            .HasDatabaseName("ux_environment_project_name");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_environment_organization");

        // Deleting a project never cascades into immediate permanent deletion of
        // what hangs underneath (Specification §6.5): the subtree is retained and
        // restored as one. A database cascade would be the opposite of that
        // promise, so there is none.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_environment_project");
    }
}
