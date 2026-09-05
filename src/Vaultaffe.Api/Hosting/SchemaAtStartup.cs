using Vaultaffe.Infrastructure.Persistence;

namespace Vaultaffe.Api.Hosting;

/// <summary>
/// Runs the migrations before anything is served (Specification §6.3). Hosted
/// services start before the server accepts a request, which is what makes
/// "upgrading is pulling an image and restarting it" true rather than a race
/// between the first request and the schema it needs.
/// </summary>
public sealed class SchemaAtStartup(SchemaMigrator migrator) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        migrator.ApplyAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
