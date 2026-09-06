using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Identities;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The claim secret an unstarted instance is claimed with (<c>docs/storage.md</c>,
/// ADR 0019). At most one row, and none at all once the first run has consumed
/// it.
/// </summary>
/// <remarks>
/// <b>No organization column, and that is the point of the table.</b> Every other
/// table in this schema carries one and is filtered by it (Specification §9);
/// this row exists precisely in the window when there is no organization to
/// carry. <c>TenancyTests</c> asserts the rule, so this exception is named here
/// and there rather than being a table somebody forgot.
/// </remarks>
public sealed class InstanceClaimConfiguration : IEntityTypeConfiguration<InstanceClaim>
{
    /// <summary>
    /// The longest a stored claim secret may be. The prefix and the encoded
    /// bytes, with room to spare rather than a count that has to be recomputed
    /// when either changes.
    /// </summary>
    public const int SecretLimit = 128;

    public void Configure(EntityTypeBuilder<InstanceClaim> builder)
    {
        builder.ToTable("instance_claim");

        builder.HasKey(c => c.Id).HasName("pk_instance_claim");

        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.Secret)
            .HasColumnName("secret")
            .HasMaxLength(SecretLimit)
            .IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
