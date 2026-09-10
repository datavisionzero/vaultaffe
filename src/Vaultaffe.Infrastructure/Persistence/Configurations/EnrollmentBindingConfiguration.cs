using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Projects;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// One project, or one environment of it, that a person agreed an enrollment may
/// reach (<c>docs/storage.md</c>) — the reach of a token that does not exist yet.
/// </summary>
/// <remarks>
/// The shape is <see cref="TokenBindingConfiguration"/>'s and the lifetime is
/// not: these rows are a decision in flight, they go with the enrollment that
/// carries them, and what the token ended up reaching is written into
/// <c>token_binding</c> when it is finally issued.
/// </remarks>
public sealed class EnrollmentBindingConfiguration : IEntityTypeConfiguration<EnrollmentBinding>
{
    public void Configure(EntityTypeBuilder<EnrollmentBinding> builder)
    {
        builder.ToTable("enrollment_binding");

        builder.HasKey(b => b.Id).HasName("pk_enrollment_binding");

        builder.Property(b => b.Id).HasColumnName("id");
        builder.Property(b => b.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(b => b.AuthorizationId).HasColumnName("authorization_id").IsRequired();
        builder.Property(b => b.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(b => b.EnvironmentId).HasColumnName("environment_id");

        // The same rule a token's binding follows, and `nulls not distinct` for
        // the same reason: without it Postgres would allow "this whole project"
        // twice, because two nulls are ordinarily unequal to each other.
        builder.HasIndex(b => new { b.AuthorizationId, b.ProjectId, b.EnvironmentId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_enrollment_binding_authorization_project_environment");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_enrollment_binding_organization");

        // A place that is genuinely gone takes the agreement about it with it.
        // Nothing is lost: an enrollment nobody collected is worth nothing an
        // hour later anyway.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(b => b.ProjectId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_enrollment_binding_project");

        builder.HasOne<Environment>()
            .WithMany()
            .HasForeignKey(b => b.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_enrollment_binding_environment");
    }
}
