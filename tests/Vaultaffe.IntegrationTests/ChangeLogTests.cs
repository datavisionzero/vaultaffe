using Microsoft.EntityFrameworkCore;
using Vaultaffe.Domain.History;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// Specification §6.5 puts the change log and the value history on opposite
/// sides of a line: the history holds values under tight bounds, and the log
/// holds none at all — not the new value, not the old one, not a diff. Doppler
/// put both into the log and had to bolt on a redaction that does not truly
/// delete. These tests are what keeps that line where it is.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ChangeLogTests(PostgresFixture postgres)
{
    /// <summary>
    /// Every column of the table, listed. A value is bytes — that is what the
    /// two tables which do hold one store — so a <c>bytea</c> column appearing
    /// here is the failure this asserts against. Adding a column means adding it
    /// to this list, which is the point: it cannot happen without somebody
    /// reading the sentence above.
    /// </summary>
    [Fact]
    public async Task The_change_log_has_no_column_a_value_could_live_in()
    {
        await using var migrated = await Migrated.EmptyAsync(postgres);
        await using var context = migrated.Writer(null);

        var table = context.Model.FindEntityType(typeof(ChangeLogEntry))!;

        Assert.Equal(
            [
                "action",
                "environment_name",
                "id",
                "identity_id",
                "identity_name",
                "identity_type",
                "occurred_at",
                "organization_id",
                "project_name",
                "secret_name",
            ],
            table.GetProperties().Select(column => column.GetColumnName()).Order(StringComparer.Ordinal));

        Assert.DoesNotContain(
            table.GetProperties(),
            column => column.ClrType == typeof(byte[]));
    }

    /// <summary>
    /// No foreign key out of this table, deliberately. Neither a purge nor an
    /// expiry removes change-log entries, so the log outlives the project,
    /// environment and secret it talks about — and a key pointing at a row that
    /// is gone would take the entry with it.
    /// </summary>
    [Fact]
    public async Task The_change_log_points_at_nothing_that_can_be_purged()
    {
        await using var migrated = await Migrated.EmptyAsync(postgres);
        await using var context = migrated.Writer(null);

        var table = context.Model.FindEntityType(typeof(ChangeLogEntry))!;

        Assert.Empty(table.GetForeignKeys());
    }

    [Fact]
    public async Task An_entry_survives_the_subject_it_describes()
    {
        await using var migrated = await Migrated.SeededAsync(postgres);

        await using (var writing = migrated.Writer(migrated.Organization.Id))
        {
            writing.Add(new ChangeLogEntry(
                Guid.NewGuid(),
                migrated.Organization.Id,
                Migrated.Now,
                Guid.NewGuid(),
                IdentityType.AgentToken,
                "quiet-otter-42",
                ChangeAction.ValueSet,
                migrated.Project.Name,
                migrated.Environment.Name,
                migrated.Secret.Name));

            var secret = await writing.Secrets.SingleAsync(TestContext.Current.CancellationToken);
            writing.Remove(secret);

            await writing.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = migrated.Writer(migrated.Organization.Id);

        var entry = await reading.ChangeLog.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("STRIPE_KEY", entry.SecretName);
        Assert.Equal(IdentityType.AgentToken, entry.IdentityType);
        Assert.Empty(await reading.Secrets.ToListAsync(TestContext.Current.CancellationToken));
    }
}
