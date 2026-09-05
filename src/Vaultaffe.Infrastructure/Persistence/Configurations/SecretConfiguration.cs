using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Secrets;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// A secret and its current value (<c>docs/storage.md</c>). The value rests
/// sealed under this secret's own data key, which is itself wrapped by the
/// instance master key (Specification §6.3) — that is what later makes rotating
/// the master key a rewrap of <c>wrapped_data_key</c> rather than a
/// re-encryption of every value in the instance.
/// </summary>
public sealed class SecretConfiguration : IEntityTypeConfiguration<Secret>
{
    public void Configure(EntityTypeBuilder<Secret> builder)
    {
        builder.ToTable("secret", table =>
        {
            table.HasCheckConstraint("ck_secret_name", $"name ~ '{SecretName.Pattern}'");

            // A value never rests half-sealed. Either the nonce, the ciphertext,
            // the moment it was written and the data key it was sealed under are
            // all there, or this secret is an empty placeholder — the state that
            // stops `run` and that a human still has to fill (Specification
            // §6.2). Without this, a half-written row would read as a value
            // nobody can open.
            table.HasCheckConstraint(
                "ck_secret_value_sealed_whole",
                """
                (ciphertext is null and nonce is null and value_written_at is null)
                or (ciphertext is not null and nonce is not null
                    and value_written_at is not null and wrapped_data_key is not null)
                """);
        });

        builder.HasKey(s => s.Id).HasName("pk_secret");

        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(s => s.EnvironmentId).HasColumnName("environment_id").IsRequired();
        builder.Property(s => s.Name)
            .HasColumnName("name")
            .HasMaxLength(SecretName.Limit)
            .IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.DeletedAt).HasColumnName("deleted_at");

        // One data key per secret, kept across every write. A data key that
        // changed per value would strand the retained versions sealed under the
        // old one, and would make a master-key rotation a re-encryption again.
        builder.Property(s => s.WrappedDataKey).HasColumnName("wrapped_data_key");
        builder.Property(s => s.Nonce).HasColumnName("nonce");
        builder.Property(s => s.Ciphertext).HasColumnName("ciphertext");
        builder.Property(s => s.ValueWrittenAt).HasColumnName("value_written_at");

        builder.Ignore(s => s.IsDeleted);
        builder.Ignore(s => s.IsPlaceholder);

        // Unique including deleted rows, for the reason in ProjectConfiguration.
        builder.HasIndex(s => new { s.EnvironmentId, s.Name })
            .IsUnique()
            .HasDatabaseName("ux_secret_environment_name");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_secret_organization");

        builder.HasOne<Environment>()
            .WithMany()
            .HasForeignKey(s => s.EnvironmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_secret_environment");
    }
}
