using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Projects;
using Vaultaffe.Domain.Secrets;
using Vaultaffe.Infrastructure.Persistence;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// A migrated database, and the rows most tests here need to say anything: an
/// organization with a project, an environment and one secret in it.
/// </summary>
internal sealed class Migrated(string connectionString) : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    public string ConnectionString { get; } = connectionString;

    public Organization Organization { get; private set; } = null!;

    public Project Project { get; private set; } = null!;

    public Environment Environment { get; private set; } = null!;

    public Secret Secret { get; private set; } = null!;

    public static async Task<Migrated> EmptyAsync(PostgresFixture postgres)
    {
        var migrated = new Migrated(await postgres.CreateDatabaseAsync());

        // Through the migrator the installation itself runs, not through
        // EnsureCreated: what a test proves about the schema should be a
        // statement about the schema an operator gets (Specification §6.3).
        await using var context = migrated.Writer(null);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        return migrated;
    }

    public static async Task<Migrated> SeededAsync(PostgresFixture postgres)
    {
        var migrated = await EmptyAsync(postgres);

        migrated.Organization = new Organization(Guid.NewGuid(), "Default", Now);
        migrated.Project = new Project(
            Guid.NewGuid(), migrated.Organization.Id, "webshop-api", Now);
        migrated.Environment = new Environment(
            Guid.NewGuid(), migrated.Organization.Id, migrated.Project.Id, "dev", Now);
        migrated.Secret = new Secret(
            Guid.NewGuid(), migrated.Organization.Id, migrated.Environment.Id, "STRIPE_KEY", Now);

        await using var context = migrated.Writer(migrated.Organization.Id);
        context.AddRange(
            migrated.Organization, migrated.Project, migrated.Environment, migrated.Secret);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return migrated;
    }

    /// <summary>
    /// A context acting inside <paramref name="organizationId"/>, or inside
    /// nothing when it is null. A fresh one per call, so that a read is a read
    /// and not the change tracker answering.
    /// </summary>
    public VaultaffeDbContext Writer(Guid? organizationId) =>
        new(
            new DbContextOptionsBuilder<VaultaffeDbContext>().UseNpgsql(ConnectionString).Options,
            new ScopeOf(organizationId));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static SchemaMigrator MigratorFor(VaultaffeDbContext context) =>
        new(
            new ServiceCollection().AddScoped(_ => context).BuildServiceProvider(),
            NullLogger<SchemaMigrator>.Instance);

    private sealed class ScopeOf(Guid? organizationId) : IOrganizationScope
    {
        public Guid? OrganizationId { get; } = organizationId;
    }
}
