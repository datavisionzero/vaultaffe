using Microsoft.Extensions.DependencyInjection;
using Vaultaffe.Application.Ports;
using Vaultaffe.Infrastructure.Identity;
using Vaultaffe.Infrastructure.Persistence;

namespace Vaultaffe.Infrastructure;

/// <summary>
/// What the composition root has to add before anybody can sign in: the rows
/// identity lives in, and the thing that hashes a password.
/// </summary>
public static class IdentityServices
{
    public static IServiceCollection AddVaultaffeIdentity(this IServiceCollection services)
    {
        services.AddScoped<IIdentityStore, IdentityStore>();

        // A singleton: it holds no state but its own parameters, and the decoy
        // hash it carries costs one Argon2id at startup instead of one per
        // sign-in that had no user to check against.
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();

        return services;
    }
}
