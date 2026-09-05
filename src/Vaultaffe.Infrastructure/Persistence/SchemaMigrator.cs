using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Vaultaffe.Infrastructure.Persistence;

/// <summary>
/// Applies the migrations an installation is missing. Specification §6.3:
/// migrations run automatically at startup, so upgrading an instance is pulling
/// an image and restarting it, and there is no second place that creates schema.
/// </summary>
/// <remarks>
/// Forward only. There is no `Down` path an operator can be told to take: a
/// self-hosted instance that rolls a schema back rolls a value's ciphertext back
/// with it, and the honest recovery from a bad upgrade is the documented backup
/// (§6.3), not a reverse migration nobody tested.
/// </remarks>
public sealed class SchemaMigrator(
    IServiceProvider services,
    ILogger<SchemaMigrator> logger)
{
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var database = scope.ServiceProvider.GetRequiredService<VaultaffeDbContext>().Database;

        var pending = (await database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("The schema is up to date.");

            return;
        }

        logger.LogInformation(
            "Applying {Count} migration(s): {Migrations}.", pending.Count, string.Join(", ", pending));

        await database.MigrateAsync(cancellationToken);

        logger.LogInformation("The schema is up to date.");
    }
}
