using Vaultaffe.Api.Hosting;
using Vaultaffe.Api.Http;
using Vaultaffe.Application.Acts;
using Vaultaffe.Application.Authorization;
using Vaultaffe.Application.Ports;
using Vaultaffe.Infrastructure;

// The composition root. Endpoints, authentication and the log sinks arrive with
// the code they belong to rather than as empty registrations placed here in
// advance.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddVaultaffePersistence(builder.Configuration);
builder.Services.AddVaultaffeEncryption(builder.Configuration);
builder.Services.AddVaultaffeIdentity();

// Who the caller is, and which organization their queries are filtered by, are
// one object answering two ports (Specification §9). A request nothing has
// authenticated is nobody, and nobody is inside no organization.
builder.Services.AddScoped<CallerContext>();
builder.Services.AddScoped<ICallerIdentity>(services => services.GetRequiredService<CallerContext>());
builder.Services.AddScoped<IOrganizationScope>(services => services.GetRequiredService<CallerContext>());

// The one place that says no (Specification §6.4). Endpoints declare what they
// need and acts ask it about a binding; neither decides anything itself.
builder.Services.AddScoped<Authority>();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<AuthenticateToken>();
builder.Services.AddScoped<StartTheInstance>();
builder.Services.AddScoped<SignIn>();
builder.Services.AddScoped<BeginDeviceLogin>();
builder.Services.AddScoped<ConfirmDeviceLogin>();
builder.Services.AddScoped<RedeemDeviceLogin>();
builder.Services.AddScoped<ChangeLog>();
builder.Services.AddScoped<CreateProject>();
builder.Services.AddScoped<ListProjects>();
builder.Services.AddScoped<ReadProject>();
builder.Services.AddScoped<RenameProject>();
builder.Services.AddScoped<DeleteProject>();
builder.Services.AddScoped<RestoreProject>();
builder.Services.AddScoped<CreateEnvironment>();
builder.Services.AddScoped<ListEnvironments>();
builder.Services.AddScoped<RenameEnvironment>();
builder.Services.AddScoped<DeleteEnvironment>();
builder.Services.AddScoped<RestoreEnvironment>();
builder.Services.AddScoped<ListSecrets>();
builder.Services.AddScoped<ReadSecret>();
builder.Services.AddScoped<SetSecret>();
builder.Services.AddScoped<DeleteSecret>();
builder.Services.AddScoped<RestoreSecret>();
builder.Services.AddScoped<ImportSecrets>();
builder.Services.AddScoped<ExportEnvironment>();
builder.Services.AddScoped<ReadChangeLog>();
builder.Services.AddScoped<ReadValueHistory>();
builder.Services.AddScoped<RollBackSecret>();
builder.Services.AddScoped<CreateToken>();
builder.Services.AddScoped<ListTokens>();
builder.Services.AddScoped<RevokeToken>();

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

// And before any endpoint runs: whoever presented a token is who they are for
// the rest of the request, filter included.
app.UseVaultaffeTokens();

// Routing is called out rather than left implicit, because what comes after it
// reads the endpoint: authorization is one middleware over all of them, and an
// endpoint is what carries the requirement it enforces (§6.4).
app.UseRouting();

app.UseVaultaffeAuthorization();

app.MapOpenApi();
app.MapContract();
app.MapIdentity();
app.MapDeviceLogin();
app.MapDevicePage();
app.MapTokens();
app.MapProjects();
app.MapSecrets();
app.MapHistory();

app.Run();

/// <summary>
/// Named so that the integration tests can start this whole application in
/// their own process, against a database of their own. Top-level statements
/// generate the class; this makes it something another assembly can name.
/// </summary>
public partial class Program;
