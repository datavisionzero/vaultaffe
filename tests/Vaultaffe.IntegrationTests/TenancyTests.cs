using Microsoft.EntityFrameworkCore;
using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Projects;
using Vaultaffe.Domain.Secrets;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// Specification §9 puts the organization on every domain table and the filter in
/// one central place. This is what makes that a fact rather than an intention:
/// the model is read to prove no table escaped the rule, and a second
/// organization is put in the database to prove the filter actually holds.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TenancyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Every_table_carries_the_organization_and_is_filtered_by_it()
    {
        await using var migrated = await Migrated.EmptyAsync(postgres);
        await using var context = migrated.Writer(Guid.NewGuid());

        foreach (var entity in context.Model.GetEntityTypes())
        {
            // The organization table is the one exception, and it is an
            // exception of shape rather than of rule — it is filtered by its own
            // key. Everything else carries the column.
            if (entity.ClrType != typeof(Organization))
            {
                Assert.True(
                    typeof(IBelongToAnOrganization).IsAssignableFrom(entity.ClrType),
                    $"{entity.ClrType.Name} is a domain table that does not carry its organization.");
            }

            Assert.True(
                entity.GetDeclaredQueryFilters().Count > 0,
                $"{entity.ClrType.Name} has no organization filter, so a query over it "
                + "would answer another organization's rows.");
        }
    }

    [Fact]
    public async Task One_organization_never_sees_another()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);

        var stranger = new Organization(Guid.NewGuid(), "Someone else", Migrated.Now);
        var theirProject = new Project(Guid.NewGuid(), stranger.Id, "landing-page", Migrated.Now);

        await using (var theirs = migrated.Writer(stranger.Id))
        {
            theirs.AddRange(stranger, theirProject);
            await theirs.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ours = migrated.Writer(migrated.Organization.Id);

        var projects = await ours.Projects.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([migrated.Project.Id], projects.Select(project => project.Id));
        Assert.Null(await ours.Projects.FindAsync(
            [theirProject.Id], TestContext.Current.CancellationToken));
        Assert.Single(await ours.Organizations.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The model is cached across context instances, and the filter reads the
    /// scope through the instance running the query. If it ever captured the
    /// scope of whichever context happened to build the model, this is the test
    /// that notices.
    /// </summary>
    [Fact]
    public async Task The_filter_follows_the_caller_and_not_the_first_context()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);

        var stranger = new Organization(Guid.NewGuid(), "Someone else", Migrated.Now);

        await using (var theirs = migrated.Writer(stranger.Id))
        {
            theirs.Add(stranger);
            await theirs.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var first = migrated.Writer(migrated.Organization.Id);
        await using var second = migrated.Writer(stranger.Id);

        Assert.Equal(
            "Default",
            (await first.Organizations.SingleAsync(TestContext.Current.CancellationToken)).Name);
        Assert.Equal(
            "Someone else",
            (await second.Organizations.SingleAsync(TestContext.Current.CancellationToken)).Name);
    }

    /// <summary>
    /// A caller nothing has authenticated is inside no organization, and the safe
    /// end of that comparison is nothing rather than everything.
    /// </summary>
    [Fact]
    public async Task A_caller_inside_no_organization_sees_nothing()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);
        await using var nobody = migrated.Writer(null);

        Assert.Empty(await nobody.Organizations.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await nobody.Projects.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await nobody.Set<Environment>().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await nobody.Secrets.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Reaching past the filter has to be possible for the one caller that runs
    /// before anybody is authenticated — the first run, which creates the default
    /// organization — and has to be an explicit act when it happens.
    /// </summary>
    [Fact]
    public async Task Ignoring_the_filter_is_available_and_deliberate()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);
        await using var nobody = migrated.Writer(null);

        Assert.Single(await nobody.Organizations
            .IgnoreQueryFilters()
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A secret's rows are reachable only through the same filter, which is the
    /// half that matters: this is the table the product exists to protect.
    /// </summary>
    [Fact]
    public async Task A_strangers_secret_is_not_reachable_by_its_key()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);

        var stranger = Guid.NewGuid();

        await using var theirs = migrated.Writer(stranger);

        Assert.Null(await theirs.Secrets.FindAsync(
            [migrated.Secret.Id], TestContext.Current.CancellationToken));
    }
}
