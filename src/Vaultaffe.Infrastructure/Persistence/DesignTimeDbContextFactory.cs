using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Vaultaffe.Application.Ports;

namespace Vaultaffe.Infrastructure.Persistence;

/// <summary>
/// What <c>dotnet ef migrations add</c> builds the model with. It exists so that
/// adding a migration needs no running instance and no connection string —
/// nothing here ever opens the connection; the provider only has to be Npgsql so
/// the generated SQL is Postgres'.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VaultaffeDbContext>
{
    public VaultaffeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<VaultaffeDbContext>()
            .UseNpgsql("Host=design-time;Database=vaultaffe")
            .Options;

        return new VaultaffeDbContext(options, new NoOrganization());
    }

    /// <summary>
    /// Nobody is calling at design time. The query filters read this and answer
    /// nothing, which is the right answer for a caller that does not exist.
    /// </summary>
    private sealed class NoOrganization : IOrganizationScope
    {
        public Guid? OrganizationId => null;
    }
}
