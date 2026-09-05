using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Projects;
using Vaultaffe.Domain.Tokens;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// One project, or one environment of one project, that a token may touch
/// (<c>docs/storage.md</c>). A token with no rows here reaches its whole
/// organization, which is what an agent token gets unless the human narrows it:
/// the point is attribution, not restriction (Specification §6.4).
/// </summary>
public sealed class TokenBindingConfiguration : IEntityTypeConfiguration<TokenBinding>
{
    public void Configure(EntityTypeBuilder<TokenBinding> builder)
    {
        builder.ToTable("token_binding");

        builder.HasKey(b => b.Id).HasName("pk_token_binding");

        builder.Property(b => b.Id).HasColumnName("id");
        builder.Property(b => b.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(b => b.TokenId).HasColumnName("token_id").IsRequired();
        builder.Property(b => b.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(b => b.EnvironmentId).HasColumnName("environment_id");

        // A token is bound to a project, or to one environment of it, at most
        // once. `nulls not distinct` is what makes the first half of that true:
        // without it Postgres would allow "this whole project" twice, because
        // two nulls are ordinarily unequal to each other.
        builder.HasIndex(b => new { b.TokenId, b.ProjectId, b.EnvironmentId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_token_binding_token_project_environment");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_token_binding_organization");

        // A binding is not a record of anything; it only narrows. When the
        // project or environment it names is genuinely gone, so is it.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(b => b.ProjectId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_token_binding_project");

        builder.HasOne<Environment>()
            .WithMany()
            .HasForeignKey(b => b.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_token_binding_environment");
    }
}
