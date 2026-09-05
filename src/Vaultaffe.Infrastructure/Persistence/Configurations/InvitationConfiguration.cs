using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// An invitation into the organization (<c>docs/storage.md</c>): who it is for,
/// what accepting it makes them, and the hash of the code in its link.
/// </summary>
public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitation", table => table.HasCheckConstraint(
            "ck_invitation_email", $"email ~ '{EmailAddress.Pattern}'"));

        builder.HasKey(invitation => invitation.Id).HasName("pk_invitation");

        builder.Property(invitation => invitation.Id).HasColumnName("id");
        builder.Property(invitation => invitation.OrganizationId)
            .HasColumnName("organization_id").IsRequired();

        builder.Property(invitation => invitation.Email)
            .HasColumnName("email")
            .HasMaxLength(EmailAddress.Limit)
            .IsRequired();

        // Not unique: an address may be invited again after the first invitation
        // was withdrawn or ran out, and the row of the first one stays — it is
        // what says who invited whom and when. What is refused is a *second open*
        // one, which is a rule about state rather than about a column.
        builder.HasIndex(invitation => invitation.Email).HasDatabaseName("ix_invitation_email");

        builder.Property(invitation => invitation.Name)
            .HasColumnName("name")
            .HasMaxLength(User.NameLimit)
            .IsRequired();

        builder.Property(invitation => invitation.IsAdministrator)
            .HasColumnName("is_administrator").IsRequired();

        // The hash and never the code, exactly as a token's value is stored
        // (ADR 0004): the instance holds no credential it could read back.
        builder.Property(invitation => invitation.CodeHash)
            .HasColumnName("code_hash")
            .HasColumnType("bytea")
            .IsRequired();

        builder.HasIndex(invitation => invitation.CodeHash)
            .IsUnique()
            .HasDatabaseName("ux_invitation_code_hash");

        builder.Property(invitation => invitation.InvitedByUserId)
            .HasColumnName("invited_by_user_id").IsRequired();

        builder.Property(invitation => invitation.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(invitation => invitation.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(invitation => invitation.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(invitation => invitation.AcceptedByUserId).HasColumnName("accepted_by_user_id");
        builder.Property(invitation => invitation.WithdrawnAt).HasColumnName("withdrawn_at");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(invitation => invitation.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_invitation_organization");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(invitation => invitation.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_invitation_invited_by");
    }
}
