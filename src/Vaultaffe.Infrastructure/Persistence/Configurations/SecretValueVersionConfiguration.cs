using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// A value a secret used to hold (<c>docs/storage.md</c>), kept so that a
/// mistake has an undo button — and kept briefly, because every retained old
/// value is usually a still-valid credential (Specification §6.5).
/// </summary>
public sealed class SecretValueVersionConfiguration
    : IEntityTypeConfiguration<SecretValueVersion>
{
    public void Configure(EntityTypeBuilder<SecretValueVersion> builder)
    {
        builder.ToTable("secret_value_version");

        builder.HasKey(v => v.Id).HasName("pk_secret_value_version");

        builder.Property(v => v.Id).HasColumnName("id");
        builder.Property(v => v.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(v => v.SecretId).HasColumnName("secret_id").IsRequired();
        builder.Property(v => v.Nonce).HasColumnName("nonce").IsRequired();
        builder.Property(v => v.Ciphertext).HasColumnName("ciphertext").IsRequired();
        builder.Property(v => v.WrittenAt).HasColumnName("written_at").IsRequired();
        builder.Property(v => v.ReplacedAt).HasColumnName("replaced_at").IsRequired();

        builder.Ignore(v => v.ExpiresAt);

        // What a rollback reads and what the retention sweep reads: the newest
        // superseded values of one secret. `replaced_at` is the clock, not
        // `written_at` — a value that stood for a year and was replaced this
        // morning is this morning's undo (Specification §6.5).
        builder.HasIndex(v => new { v.SecretId, v.ReplacedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_secret_value_version_secret_replaced_at");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(v => v.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_secret_value_version_organization");

        // The one cascade in this schema, and it does not contradict the
        // recovery window: a deleted secret keeps its row, so this fires only
        // when that row is genuinely removed — a human purge, or the end of the
        // window. When the secret is gone its history has to be gone with it;
        // that is what a purge means (Specification §6.5).
        builder.HasOne<Secret>()
            .WithMany()
            .HasForeignKey(v => v.SecretId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_secret_value_version_secret");
    }
}
