using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.References;
using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The change log (<c>docs/storage.md</c>): when, where, what, and by whom —
/// with the identity's type beside it, because with writing agents that is the
/// interesting question (Specification §6.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no value column here, and adding one is the bug.</b> Not the new
/// value, not the old one, not a diff. Doppler puts both into the log and had to
/// bolt on a redaction that does not truly delete; we follow AWS instead.
/// <c>ChangeLogTests</c> reads the table out of the model and fails if a column
/// that could hold a value ever appears.
/// </para>
/// <para>
/// Everything is recorded by name and there is no foreign key out of this table
/// at all — not to a project, environment or secret, and not to a person or a
/// token. Neither a purge nor an expiry removes change-log entries, so this
/// table outlives the rows it talks about, and a key pointing at one that is
/// gone would take the entry with it. A deactivated person and a revoked token
/// stay in the log for the same reason, reading as a name rather than as a
/// bare id.
/// </para>
/// </remarks>
public sealed class ChangeLogEntryConfiguration : IEntityTypeConfiguration<ChangeLogEntry>
{
    /// <summary>The longest an identity's recorded name may be.</summary>
    public const int IdentityNameLimit = 200;

    /// <summary>
    /// The longest the subject of an administrative entry may be. An email
    /// address is the longest thing that goes in it.
    /// </summary>
    public const int AboutNameLimit = 320;

    public void Configure(EntityTypeBuilder<ChangeLogEntry> builder)
    {
        builder.ToTable("change_log_entry", table => table.HasCheckConstraint(
            "ck_change_log_entry_identity_type", "identity_type between 1 and 3"));

        builder.HasKey(e => e.Id).HasName("pk_change_log_entry");

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder.Property(e => e.IdentityId).HasColumnName("identity_id").IsRequired();
        builder.Property(e => e.IdentityType)
            .HasColumnName("identity_type")
            .HasConversion<int>()
            .IsRequired();

        // What the identity was called at the time, so a revoked token's entries
        // still read as something other than a bare id.
        builder.Property(e => e.IdentityName)
            .HasColumnName("identity_name")
            .HasMaxLength(IdentityNameLimit)
            .IsRequired();

        builder.Property(e => e.Action)
            .HasColumnName("action")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.ProjectName)
            .HasColumnName("project_name")
            .HasMaxLength(ReferenceName.Limit);
        builder.Property(e => e.EnvironmentName)
            .HasColumnName("environment_name")
            .HasMaxLength(ReferenceName.Limit);
        builder.Property(e => e.SecretName)
            .HasColumnName("secret_name")
            .HasMaxLength(SecretName.Limit);

        // Who or what an entry was about where that is not a place in the vault:
        // an address, a token's name, an organization's new name (ADR 0020).
        // Long enough for the longest of those, which is an email address.
        builder.Property(e => e.AboutName)
            .HasColumnName("about_name")
            .HasMaxLength(AboutNameLimit);

        // What a reader of the log asks for: one organization, newest first.
        builder.HasIndex(e => new { e.OrganizationId, e.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_change_log_entry_organization_occurred_at");
    }
}
