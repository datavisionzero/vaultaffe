using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vaultaffe.Infrastructure;
using Vaultaffe.Infrastructure.Encryption;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The whole instance in the test process, on a database of its own, with the
/// environment an operator would have set for the first start.
/// </summary>
/// <remarks>
/// Starting the host runs the migrations, exactly as it does for an operator
/// (Specification §6.3), so what a test asks here is asked of an installation
/// rather than of a handler someone called directly. The two configured values
/// are always set — the connection string to a fresh database, the master key to
/// one made for this run — so that nothing in the developer's own environment
/// can reach a test.
/// </remarks>
internal sealed class AnInstance(string connectionString, string masterKey)
    : WebApplicationFactory<Program>
{
    public static async Task<AnInstance> StartedAsync(PostgresFixture postgres) =>
        new(await postgres.CreateDatabaseAsync(), AMasterKey());

    /// <summary>The address the first user of a started instance signs in with.</summary>
    public const string Administrator = "maintainer@example.test";

    /// <summary>Their password. Long enough to be one, and nothing else about it matters.</summary>
    public const string AdministratorPassword = "a-password-of-real-length";

    public string ConnectionString => connectionString;

    /// <summary>
    /// The instance after its first run, and the session token that came out of
    /// it — which is what everything else here acts under.
    /// </summary>
    public async Task<string> StartAsync()
    {
        using var client = ClientAnnouncing(null);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/instance",
            new { email = Administrator, name = "Maintainer", password = AdministratorPassword },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var started = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        return started["session"]!["token"]!.GetValue<string>();
    }

    /// <summary>
    /// What the instance logged at error or above — the exception behind a 500,
    /// which the problem document deliberately does not carry.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    private readonly List<string> _errors = [];

    /// <summary>
    /// A client that announces <paramref name="client"/> as its release, or none
    /// at all when it is null — which is what a browser does.
    /// </summary>
    public HttpClient ClientAnnouncing(string? client)
    {
        var http = CreateClient();

        if (client is not null)
        {
            http.DefaultRequestHeaders.Add("Vaultaffe-Client", client);
        }

        return http;
    }

    /// <summary>A client presenting <paramref name="token"/>, or none at all.</summary>
    public HttpClient ClientWith(string? token)
    {
        var http = CreateClient();

        if (token is not null)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return http;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{PersistenceServices.ConnectionStringName}"] = connectionString,
                [EncryptionServices.MasterKeyName] = masterKey,
            }));

        builder.ConfigureLogging(logging => logging.AddProvider(new Capture(_errors)));

        return base.CreateHost(builder);
    }

    private static string AMasterKey() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(MasterKey.Length));

    private sealed class Capture(List<string> errors) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                lock (errors)
                {
                    errors.Add($"{formatter(state, exception)}\n{exception}");
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
