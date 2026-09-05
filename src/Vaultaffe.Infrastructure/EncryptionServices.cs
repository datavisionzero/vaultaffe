using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaultaffe.Application.Ports;
using Vaultaffe.Infrastructure.Encryption;

namespace Vaultaffe.Infrastructure;

/// <summary>
/// What the composition root has to add before a value can be written or read.
/// The master key is the operator's, exactly as the connection string is, and it
/// arrives the same way — one `.env` for the instance (Specification §6.3).
/// </summary>
public static class EncryptionServices
{
    /// <summary>
    /// The configuration key an installation sets its master key under. In the
    /// environment that is <c>Vaultaffe__MasterKey</c>.
    /// </summary>
    public const string MasterKeyName = "Vaultaffe:MasterKey";

    /// <summary>
    /// Reads the master key here, at startup, rather than at the first write: an
    /// instance that would refuse every value after taking them for an hour is
    /// worse than one that does not start.
    /// </summary>
    public static IServiceCollection AddVaultaffeEncryption(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(
            MasterKey.FromConfiguredValue(configuration[MasterKeyName], "Vaultaffe__MasterKey"));

        services.AddSingleton<IKeyRing, KeyRing>();

        return services;
    }
}
