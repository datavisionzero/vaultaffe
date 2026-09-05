using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// One `vaultaffe login` in progress (<c>docs/storage.md</c>): the hash of the
/// long code the CLI polls with, the short code a human types, and what has
/// happened to it.
/// </summary>
public sealed class DeviceAuthorizationConfiguration
    : IEntityTypeConfiguration<DeviceAuthorization>
{
    public void Configure(EntityTypeBuilder<DeviceAuthorization> builder)
    {
        builder.ToTable("device_authorization");

        builder.HasKey(d => d.Id).HasName("pk_device_authorization");

        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.OrganizationId).HasColumnName("organization_id").IsRequired();

        // The hash, never the code — the same rule the token table follows, and
        // for the same reason: this one is a credential for ten minutes.
        builder.Property(d => d.DeviceCodeHash).HasColumnName("device_code_hash").IsRequired();
        builder.HasIndex(d => d.DeviceCodeHash)
            .IsUnique()
            .HasDatabaseName("ux_device_authorization_device_code_hash");

        // The user code is stored in the clear, because it is not a credential:
        // it names a pending request to the human confirming it, and confirming
        // still takes their password. Unique so that two logins in flight can
        // never be told apart only by luck.
        builder.Property(d => d.UserCode)
            .HasColumnName("user_code")
            .HasMaxLength(UserCode.Length)
            .IsRequired();
        builder.HasIndex(d => d.UserCode)
            .IsUnique()
            .HasDatabaseName("ux_device_authorization_user_code");

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(d => d.ApprovedAt).HasColumnName("approved_at");
        builder.Property(d => d.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(d => d.DeniedAt).HasColumnName("denied_at");
        builder.Property(d => d.RedeemedAt).HasColumnName("redeemed_at");
        builder.Property(d => d.IssuedTokenId).HasColumnName("issued_token_id");

        // Which rows a sweep will take away when this instance has one: expired
        // and collected logins are rubbish within the hour.
        builder.HasIndex(d => d.ExpiresAt).HasDatabaseName("ix_device_authorization_expires_at");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(d => d.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_device_authorization_organization");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_device_authorization_approved_by");
    }
}
