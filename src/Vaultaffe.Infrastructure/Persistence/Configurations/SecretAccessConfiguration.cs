using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// First and last use of a secret by an identity (<c>docs/storage.md</c>).
/// </summary>
/// <remarks>
/// Two moments per pair and never a row per read: the table is bounded by how
/// many identities an organization has, not by how often anything runs. That is
/// what makes it a summary rather than the execution history Specification §6.5
/// declines to promise — and it is why there is no column here a value could go
/// in.
/// <para>
/// The identity is a plain id with its type and name beside it, as in the change
/// log and for the same reason: a revoked token's line has to keep reading as
/// something.
/// </para>
/// </remarks>
public sealed class SecretAccessConfiguration : IEntityTypeConfiguration<SecretAccess>
{
    public void Configure(EntityTypeBuilder<SecretAccess> builder)
    {
        builder.ToTable("secret_access", table =>
            table.HasCheckConstraint("ck_secret_access_order", "last_at >= first_at"));

        builder.HasKey(a => a.Id).HasName("pk_secret_access");

        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(a => a.SecretId).HasColumnName("secret_id").IsRequired();
        builder.Property(a => a.IdentityId).HasColumnName("identity_id").IsRequired();
        builder.Property(a => a.IdentityType).HasColumnName("identity_type").IsRequired();
        builder.Property(a => a.IdentityName)
            .HasColumnName("identity_name")
            .HasMaxLength(256)
            .IsRequired();
        builder.Property(a => a.FirstAt).HasColumnName("first_at").IsRequired();
        builder.Property(a => a.LastAt).HasColumnName("last_at").IsRequired();

        // The pair this table is keyed by in everything but name, and what the
        // `on conflict` of the recording statement names.
        builder.HasIndex(a => new { a.SecretId, a.IdentityId })
            .IsUnique()
            .HasDatabaseName("ux_secret_access_secret_identity");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_secret_access_organization");

        // A cascade, like the retained values and for the same reason: this hangs
        // off a secret rather than between the containers, and when a secret is
        // genuinely gone what was recorded about reading it has to be gone too.
        // That is what a purge means (§6.5).
        builder.HasOne<Secret>()
            .WithMany()
            .HasForeignKey(a => a.SecretId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_secret_access_secret");
    }
}
