using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// A person in an organization (<c>docs/storage.md</c>): the address they sign in
/// with, their password hash, and whether they administer the organization.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>
    /// What a stored password hash may be. Argon2id's encoded form is well under
    /// this; the room is for a later algorithm with longer parameters, because a
    /// column that has to be widened is a migration nobody wanted.
    /// </summary>
    public const int PasswordHashLimit = 512;

    public void Configure(EntityTypeBuilder<User> builder)
    {
        // `app_user` and not `user`: `user` is a reserved word in Postgres and
        // every query against it would need quoting for the rest of this
        // product's life. Every other table here is named after its concept, and
        // this is the one place that costs a prefix.
        builder.ToTable("app_user", table => table.HasCheckConstraint(
            "ck_user_email", $"email ~ '{EmailAddress.Pattern}'"));

        builder.HasKey(u => u.Id).HasName("pk_user");

        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.OrganizationId).HasColumnName("organization_id").IsRequired();

        // Normalized and lower-case in the domain, so the index and the lookup
        // agree without either of them saying anything about case.
        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(EmailAddress.Limit)
            .IsRequired();

        // Unique across the instance rather than per organization. With one
        // organization the two are the same thing; with several they are not, and
        // a sign-in has only an address to go on — an address that belonged to two
        // people would be a login nobody could resolve.
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ux_user_email");

        builder.Property(u => u.Name)
            .HasColumnName("name")
            .HasMaxLength(User.NameLimit)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(PasswordHashLimit)
            .IsRequired();

        builder.Property(u => u.IsAdministrator).HasColumnName("is_administrator").IsRequired();
        builder.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();

        // Null while they are in the organization. Nullable rather than a
        // boolean, because "since when" is the question anybody asks about a
        // person who is no longer here, and a flag cannot answer it.
        builder.Property(u => u.DeactivatedAt).HasColumnName("deactivated_at");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_user_organization");
    }
}
