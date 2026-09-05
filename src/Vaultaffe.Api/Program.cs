// The composition root, and for now the host and nothing else. Endpoints,
// authentication, the migrator and the log sinks arrive with the code they
// belong to rather than as empty registrations placed here in advance.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();
