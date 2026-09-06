using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Secrets;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// A key the missing-key notice was told not to mention in this environment
/// again (<c>docs/storage.md</c>).
/// </summary>
/// <remarks>
/// The only table here that holds a preference rather than a fact about the
/// vault, and it is deliberately the thinnest one: an environment, a key, and
/// when somebody said so. Withdrawing a dismissal deletes the row, which is why
/// there is no <c>deleted_at</c> on it — nothing about this is recoverable, and
/// nothing about it needs to be.
/// <para>
/// The name is checked against the same pattern as a secret's, so that a
/// dismissal can only ever be about a key that could exist.
/// </para>
/// </remarks>
public sealed class DismissedKeyConfiguration : IEntityTypeConfiguration<DismissedKey>
{
    public void Configure(EntityTypeBuilder<DismissedKey> builder)
    {
        builder.ToTable("dismissed_key", table =>
            table.HasCheckConstraint("ck_dismissed_key_name", $"name ~ '{SecretName.Pattern}'"));

        builder.HasKey(d => d.Id).HasName("pk_dismissed_key");

        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(d => d.EnvironmentId).HasColumnName("environment_id").IsRequired();
        builder.Property(d => d.Name)
            .HasColumnName("name")
            .HasMaxLength(SecretName.Limit)
            .IsRequired();
        builder.Property(d => d.DismissedAt).HasColumnName("dismissed_at").IsRequired();

        // One dismissal per key and environment. Dismissing twice is the same
        // answer twice, and the database says so rather than the act hoping.
        builder.HasIndex(d => new { d.EnvironmentId, d.Name })
            .IsUnique()
            .HasDatabaseName("ux_dismissed_key_environment_name");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(d => d.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_dismissed_key_organization");

        // `restrict`, like everything else hanging off a container: what an
        // environment leaves behind when it is purged goes explicitly, in the act
        // that asked for the purge, so that this schema keeps its one cascade
        // (`docs/storage.md`). A dismissal follows its environment all the same —
        // it is about a place that no longer exists.
        builder.HasOne<Environment>()
            .WithMany()
            .HasForeignKey(d => d.EnvironmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_dismissed_key_environment");
    }
}
