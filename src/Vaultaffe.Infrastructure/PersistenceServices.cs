using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaultaffe.Application.Ports;
using Vaultaffe.Infrastructure.Persistence;

namespace Vaultaffe.Infrastructure;

/// <summary>
/// What the composition root has to add to reach Postgres. Everything the
/// application asks for through a port is answered from here, and the connection
/// string is the one piece of it that is the operator's.
/// </summary>
public static class PersistenceServices
{
    /// <summary>The configuration key an installation sets its database under.</summary>
    public const string ConnectionStringName = "Postgres";

    public static IServiceCollection AddVaultaffePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"No connection string named '{ConnectionStringName}'. An installation sets "
                + $"ConnectionStrings__{ConnectionStringName} in its environment.");

        services.AddDbContext<VaultaffeDbContext>(options => options.UseNpgsql(connectionString));

        // Both scoped, over the one context of the request: what the acts change
        // and what the change log records about it reach the database in the same
        // transaction (docs/storage.md).
        services.AddScoped<IProjectStore, ProjectStore>();
        services.AddScoped<ISecretStore, SecretStore>();
        services.AddScoped<IChangeLogStore, ChangeLogStore>();

        services.AddSingleton<SchemaMigrator>();

        return services;
    }
}
