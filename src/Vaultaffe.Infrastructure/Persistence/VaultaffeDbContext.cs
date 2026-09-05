using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Projects;
using Vaultaffe.Domain.Secrets;
using Vaultaffe.Domain.Tokens;
// The domain calls it what the product calls it, and that word is taken in
// every file that also uses the base class library's. One alias here is the
// price; a domain type named EnvironmentEntity would have been the alternative.
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Infrastructure.Persistence;

/// <summary>
/// The one place that declares schema, and the one place that keeps two
/// organizations apart.
/// </summary>
/// <remarks>
/// Specification §9: every domain table carries the organization and all queries
/// are organization-filtered by default, enforced centrally rather than in every
/// handler. <see cref="OnModelCreating"/> is that centre — it walks the model and
/// puts the same filter on every entity implementing
/// <see cref="IBelongToAnOrganization"/>, so a table added tomorrow is filtered
/// by existing in the model rather than by someone remembering to say so.
/// <c>TenancyTests</c> fails the build if one ever is not.
/// </remarks>
public sealed class VaultaffeDbContext(
    DbContextOptions<VaultaffeDbContext> options,
    IOrganizationScope scope) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Environment> Environments => Set<Environment>();

    public DbSet<Secret> Secrets => Set<Secret>();

    public DbSet<SecretValueVersion> SecretValueVersions => Set<SecretValueVersion>();

    public DbSet<Token> Tokens => Set<Token>();

    public DbSet<TokenBinding> TokenBindings => Set<TokenBinding>();

    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();

    /// <summary>
    /// The organization the caller acts in. Read by the query filters below on
    /// every query, so it follows the caller of the request this context was
    /// resolved for rather than whoever happened to build the cached model.
    /// </summary>
    private IOrganizationScope Scope { get; } = scope;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(VaultaffeDbContext).Assembly);

        // The organizations table is the one exception, and it is an exception
        // of shape rather than of rule: a row of it *is* an organization and
        // carries no key pointing at one, so it is filtered by its own.
        builder.Entity<Organization>()
            .HasQueryFilter(organization => organization.Id == Scope.OrganizationId);

        var applyToOneTable = typeof(VaultaffeDbContext)
            .GetMethod(nameof(OnlyTheCallersOrganization), BindingFlags.Instance | BindingFlags.NonPublic)!;

        foreach (var entity in builder.Model.GetEntityTypes().ToList())
        {
            if (typeof(IBelongToAnOrganization).IsAssignableFrom(entity.ClrType))
            {
                applyToOneTable.MakeGenericMethod(entity.ClrType).Invoke(this, [builder]);
            }
        }
    }

    /// <summary>
    /// <c>row =&gt; row.OrganizationId == Scope.OrganizationId</c>, for one table.
    /// </summary>
    /// <remarks>
    /// A caller with no organization yet — the first run, a request nothing has
    /// authenticated — matches nothing, because a null <c>Guid?</c> never equals
    /// a column. Answering nothing is the safe end of that comparison; answering
    /// everything is how this rule fails silently.
    /// </remarks>
    private void OnlyTheCallersOrganization<T>(ModelBuilder builder)
        where T : class, IBelongToAnOrganization =>
        builder.Entity<T>().HasQueryFilter(row => row.OrganizationId == Scope.OrganizationId);
}
