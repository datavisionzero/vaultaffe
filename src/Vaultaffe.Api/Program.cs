using Vaultaffe.Api.Hosting;
using Vaultaffe.Application.Ports;
using Vaultaffe.Infrastructure;

// The composition root. Endpoints, authentication and the log sinks arrive with
// the code they belong to rather than as empty registrations placed here in
// advance.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddVaultaffePersistence(builder.Configuration);
builder.Services.AddVaultaffeEncryption(builder.Configuration);

// Nothing authenticates a caller yet, so nobody is inside an organization and
// every query filter answers nothing. That is the safe end of the comparison
// (Specification §9) and it is replaced, not amended, when identity arrives.
builder.Services.AddScoped<IOrganizationScope, NobodyYet>();

builder.Services.AddHostedService<SchemaAtStartup>();

var app = builder.Build();

app.Run();

/// <summary>
/// The organization scope until there is something to read it from. It answers
/// null rather than a default organization on purpose: a caller nothing has
/// authenticated should see nothing, not everything.
/// </summary>
internal sealed class NobodyYet : IOrganizationScope
{
    public Guid? OrganizationId => null;
}
