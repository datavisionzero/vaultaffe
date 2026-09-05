using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The credential's own record (<c>docs/storage.md</c>): what it may touch, what
/// it may do, and whether it still counts. Who owns it is not here — that
/// arrives with the identity it belongs to.
/// </summary>
public sealed class TokenConfiguration : IEntityTypeConfiguration<Token>
{
    /// <summary>The longest a human may name a token.</summary>
    public const int NameLimit = 100;

    public void Configure(EntityTypeBuilder<Token> builder)
    {
        builder.ToTable("token", table => table.HasCheckConstraint(
            "ck_token_kind", "kind between 1 and 3"));

        builder.HasKey(t => t.Id).HasName("pk_token");

        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(t => t.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(NameLimit);

        // The hash, never the value. A token this instance could read back would
        // be a credential it holds in the clear, which is the opposite of what it
        // exists for; the value is shown exactly once, at creation
        // (Specification §6.1). Unique, because authentication looks a token up
        // by exactly this column.
        builder.Property(t => t.ValueHash).HasColumnName("value_hash").IsRequired();
        builder.HasIndex(t => t.ValueHash)
            .IsUnique()
            .HasDatabaseName("ux_token_value_hash");

        // The scope set of Specification §5 — `names`, `read`, `write`,
        // `delete` — stored as the int its [Flags] enum already is, so that
        // "may write but never read" is one column and adding a scope later is a
        // new flag rather than a migration.
        builder.Property(t => t.Scopes).HasColumnName("scopes").HasConversion<int>().IsRequired();

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at");

        // Revoked rather than deleted, so that everything a token ever signed in
        // the change log keeps an author.
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");

        builder.Ignore(t => t.ReachesTheWholeOrganization);

        builder.HasMany(t => t.Bindings)
            .WithOne()
            .HasForeignKey(b => b.TokenId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_token_binding_token");

        builder.Navigation(t => t.Bindings)
            .HasField("_bindings")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_token_organization");
    }
}
