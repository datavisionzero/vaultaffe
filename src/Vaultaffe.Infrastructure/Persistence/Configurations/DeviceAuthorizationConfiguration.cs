using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// One handover in progress (<c>docs/storage.md</c>): the hash of the long code
/// the CLI polls with, the short code a human types, what has happened to it,
/// and — for an enrollment — what a person agreed the token at the end of it may
/// do.
/// </summary>
public sealed class DeviceAuthorizationConfiguration
    : IEntityTypeConfiguration<DeviceAuthorization>
{
    public void Configure(EntityTypeBuilder<DeviceAuthorization> builder)
    {
        builder.ToTable("device_authorization", table => table.HasCheckConstraint(
            "ck_device_authorization_produces", "produces between 1 and 2"));

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

        // A session for the person who confirms, or a token of the asking
        // machine's own. One table for both, because everything before that last
        // step is the same protocol (ADR 0021).
        builder.Property(d => d.Produces)
            .HasColumnName("produces")
            .HasConversion<int>()
            .IsRequired();

        // What the asking client says it is. Untrusted text, shown to a person
        // and checked by nobody — which is why the length is capped here and the
        // person who confirms is the one who settles the name.
        builder.Property(d => d.RequestedName)
            .HasColumnName("requested_name")
            .HasMaxLength(TokenConfiguration.NameLimit);

        // And what they settled on calling it. Beside the requested name rather
        // than over it: what a machine claimed about itself and what a person
        // decided are two facts, and the second is the one the token carries.
        builder.Property(d => d.ApprovedName)
            .HasColumnName("approved_name")
            .HasMaxLength(TokenConfiguration.NameLimit);

        // What that person agreed it may do, as the same integer of flags the
        // token table holds. Null until somebody has agreed anything, and for a
        // login always.
        builder.Property(d => d.ApprovedScopes)
            .HasColumnName("approved_scopes")
            .HasConversion<int?>();

        builder.HasMany(d => d.ApprovedBindings)
            .WithOne()
            .HasForeignKey(b => b.AuthorizationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_enrollment_binding_authorization");

        builder.Navigation(d => d.ApprovedBindings)
            .HasField("_approvedBindings")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

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
