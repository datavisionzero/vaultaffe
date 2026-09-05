using Npgsql;
using Testcontainers.PostgreSql;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// There is no schema to test yet. What this asserts is that the harness every
/// later integration test is written against starts at all — Testcontainers
/// pulls postgres, the container becomes reachable, and a connection answers.
/// Worth finding out while a red trunk is still cheap (ADR 0001).
/// </summary>
public sealed class HarnessTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();

    public ValueTask InitializeAsync() => new(_postgres.StartAsync());

    public ValueTask DisposeAsync() => new(_postgres.DisposeAsync().AsTask());

    [Fact]
    public async Task Postgres_starts_and_answers()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("select 1", connection);

        Assert.Equal(1, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
