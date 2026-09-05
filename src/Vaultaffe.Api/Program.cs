using Vaultaffe.Api.Hosting;
using Vaultaffe.Api.Http;
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

// Every refusal is a problem document, including the ones nobody wrote a handler
// for: an exception that escaped an endpoint becomes `internal` and the reason
// goes to the log rather than to the caller (docs/api.md).
builder.Services.AddExceptionHandler<Problems.Handler>();
builder.Services.AddProblemDetails();

builder.Services.AddVaultaffeOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

// Before anything else answers: a client too old to be served is told that here
// rather than at a field it did not expect (Specification §6.3).
app.UseVaultaffeVersionExchange();

app.MapOpenApi();
app.MapContract();

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

/// <summary>
/// Named so that the integration tests can start this whole application in
/// their own process, against a database of their own. Top-level statements
/// generate the class; this makes it something another assembly can name.
/// </summary>
public partial class Program;
