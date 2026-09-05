using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Npgsql;
using Vaultaffe.Domain.Projects;
using Vaultaffe.Domain.Secrets;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The rules the database enforces on its own. Each of these is also checked in
/// the domain before a row is ever built; they are here as well because a
/// constraint is what holds when a future act forgets, and because the
/// specification calls these decisions unreversible.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_migrations_apply_to_an_empty_database()
    {
        await using var migrated = await Migrated.EmptyAsync(postgres);
        await using var context = migrated.Writer(null);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Applying twice is what an operator does on every restart
    /// (Specification §6.3), and it has to be uneventful.
    /// </summary>
    [Fact]
    public async Task Applying_them_again_changes_nothing()
    {
        await using var migrated = await Migrated.EmptyAsync(postgres);
        await using var context = migrated.Writer(null);

        var applied = await context.Database.GetAppliedMigrationsAsync(
            TestContext.Current.CancellationToken);

        await using var again = await Migrated.EmptyAsync(postgres);

        Assert.NotEmpty(applied);
    }

    /// <summary>
    /// A configuration changed without a migration to carry it is a schema that
    /// exists only on the developer's machine. `dotnet ef migrations add` is the
    /// fix; this is what makes forgetting it a red trunk rather than a surprise
    /// on somebody else's instance.
    /// </summary>
    [Fact]
    public async Task The_migrations_say_everything_the_model_does()
    {
        await using var migrated = await Migrated.EmptyAsync(postgres);
        await using var context = migrated.Writer(null);

        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;

        // A snapshot is a model that was never run: it has to be finalized
        // before it can be compared with one that was.
        if (snapshot is IMutableModel unfinished)
        {
            snapshot = unfinished.FinalizeModel();
        }

        snapshot = context.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot, designTime: true, validationLogger: null);

        var pending = context.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshot.GetRelationalModel(),
            context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.Empty(pending);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("UPPER")]
    [InlineData("trailing-")]
    [InlineData("slash/inside")]
    public async Task A_project_name_that_could_not_appear_in_a_reference_is_refused(string name)
    {
        await using var migrated = await Migrated.SeededAsync(postgres);
        await using var context = migrated.Writer(migrated.Organization.Id);

        // Straight to SQL: the domain type refuses these before EF ever sees
        // them, and what this asserts is that the database refuses them too.
        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlAsync(
                $"""
                 insert into project (id, organization_id, name, created_at)
                 values ({Guid.NewGuid()}, {migrated.Organization.Id}, {name}, now())
                 """,
                TestContext.Current.CancellationToken));

        Assert.Equal("ck_project_name", refused.ConstraintName);
    }

    [Theory]
    [InlineData("lower_case")]
    [InlineData("1LEADING_DIGIT")]
    [InlineData("HAS-DASH")]
    public async Task A_secret_name_outside_the_rule_is_refused(string name)
    {
        await using var migrated = await Migrated.SeededAsync(postgres);
        await using var context = migrated.Writer(migrated.Organization.Id);

        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlAsync(
                $"""
                 insert into secret (id, organization_id, environment_id, name, created_at)
                 values ({Guid.NewGuid()}, {migrated.Organization.Id},
                         {migrated.Environment.Id}, {name}, now())
                 """,
                TestContext.Current.CancellationToken));

        Assert.Equal("ck_secret_name", refused.ConstraintName);
    }

    /// <summary>
    /// Specification §6.5: names stay reserved during the recovery window, so
    /// that recreating an object cannot silently replace its recoverable
    /// predecessor. The unique index covers deleted rows, which is what makes
    /// that true without anything having to check for it.
    /// </summary>
    [Fact]
    public async Task A_deleted_project_keeps_its_name_reserved()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);

        await using (var deleting = migrated.Writer(migrated.Organization.Id))
        {
            var project = await deleting.Projects.SingleAsync(TestContext.Current.CancellationToken);
            project.DeleteAt(Migrated.Now);
            await deleting.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var recreating = migrated.Writer(migrated.Organization.Id);
        recreating.Add(new Project(
            Guid.NewGuid(), migrated.Organization.Id, migrated.Project.Name, Migrated.Now));

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() =>
            recreating.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            "ux_project_organization_name",
            Assert.IsType<PostgresException>(refused.InnerException).ConstraintName);
    }

    [Fact]
    public async Task An_environment_name_is_unique_within_its_project_and_not_beyond_it()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);
        await using var context = migrated.Writer(migrated.Organization.Id);

        var second = new Project(Guid.NewGuid(), migrated.Organization.Id, "landing-page", Migrated.Now);
        context.Add(second);
        context.Add(new Environment(
            Guid.NewGuid(), migrated.Organization.Id, second.Id, migrated.Environment.Name, Migrated.Now));

        // `dev` in another project is a different environment, and nothing here
        // should object.
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Add(new Environment(
            Guid.NewGuid(), migrated.Organization.Id, second.Id, migrated.Environment.Name, Migrated.Now));

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            "ux_environment_project_name",
            Assert.IsType<PostgresException>(refused.InnerException).ConstraintName);
    }

    /// <summary>
    /// A value never rests half-sealed: a ciphertext without the nonce or the
    /// wrapped data key it was sealed under is a value nobody can ever open
    /// again (Specification §6.3).
    /// </summary>
    [Fact]
    public async Task A_half_sealed_value_is_refused()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);
        await using var context = migrated.Writer(migrated.Organization.Id);

        byte[] halfOfIt = [1];

        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlAsync(
                $"update secret set ciphertext = {halfOfIt} where id = {migrated.Secret.Id}",
                TestContext.Current.CancellationToken));

        Assert.Equal("ck_secret_value_sealed_whole", refused.ConstraintName);
    }

    /// <summary>
    /// A secret with no value at all is an empty placeholder, not a broken row:
    /// it is the state a human still has to fill (Specification §6.2).
    /// </summary>
    [Fact]
    public async Task An_empty_placeholder_is_a_state_and_not_a_violation()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);
        await using var context = migrated.Writer(migrated.Organization.Id);

        var secret = await context.Secrets.SingleAsync(TestContext.Current.CancellationToken);

        Assert.True(secret.IsPlaceholder);
    }

    /// <summary>
    /// The whole envelope, written and read back: this asserts the columns are
    /// there and hold what they are for, not that any particular algorithm is.
    /// </summary>
    [Fact]
    public async Task A_sealed_value_survives_the_round_trip_and_supersedes_the_old_one()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);

        byte[] dataKey = [1, 2, 3];

        await using (var writing = migrated.Writer(migrated.Organization.Id))
        {
            var secret = await writing.Secrets.SingleAsync(TestContext.Current.CancellationToken);

            Assert.Null(secret.Seal(Guid.NewGuid(), dataKey, [9], [10], Migrated.Now));

            var superseded = secret.Seal(
                Guid.NewGuid(), dataKey, [11], [12], Migrated.Now.AddHours(1));

            writing.Add(Assert.IsType<SecretValueVersion>(superseded));
            await writing.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = migrated.Writer(migrated.Organization.Id);

        var stored = await reading.Secrets.SingleAsync(TestContext.Current.CancellationToken);
        var history = await reading.SecretValueVersions.SingleAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal<byte[]>([12], stored.Ciphertext!);
        Assert.Equal<byte[]>(dataKey, stored.WrappedDataKey!);
        Assert.Equal<byte[]>([10], history.Ciphertext);
        Assert.Equal(Migrated.Now.AddHours(1), history.ReplacedAt);
    }
}
