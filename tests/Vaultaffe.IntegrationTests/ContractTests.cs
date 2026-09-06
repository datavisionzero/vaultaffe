using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The contract as a running instance serves it, against the one checked in
/// (ADR 0006). CI makes the same comparison against a real Postgres; this makes
/// it before the push, so that a changed shape is a red test on the desk and not
/// a red trunk.
/// </summary>
/// <remarks>
/// The comparison is structural, not textual: what the two documents say has to
/// agree, not how they were formatted. Regenerating is the same test with
/// <c>VAULTAFFE_CAPTURE_CONTRACT=1</c>, which writes the served document over the
/// checked-in one — formatted the way CI's capture step formats it, so that the
/// two never differ by whitespace — and then passes.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ContractTests(PostgresFixture postgres)
{
    private const string CaptureVariable = "VAULTAFFE_CAPTURE_CONTRACT";

    [Fact]
    public async Task The_document_is_served_without_a_token_and_names_every_endpoint()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing(null);

        using var response = await client.GetAsync(
            "/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("vaultaffe", document["info"]!["title"]!.GetValue<string>());

        // The version of the contract, not of the instance: this document
        // describes what /api/v1 is, and a release does not change that.
        Assert.Equal("v1", document["info"]!["version"]!.GetValue<string>());

        // Whoever captured it was at some address; nobody else is.
        Assert.Null(document["servers"]);

        var paths = document["paths"]!.AsObject()
            .Select(path => path.Key)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "/api/handshake",
                "/api/v1/changes",
                "/api/v1/device/authorizations", "/api/v1/device/tokens",
                "/api/v1/instance",
                "/api/v1/invitations", "/api/v1/invitations/acceptance",
                "/api/v1/invitations/offer", "/api/v1/invitations/{id}",
                "/api/v1/me", "/api/v1/me/email", "/api/v1/me/password",
                "/api/v1/organization",
                "/api/v1/projects", "/api/v1/projects/{project}",
                "/api/v1/projects/{project}/environments",
                "/api/v1/projects/{project}/environments/{environment}",
                "/api/v1/projects/{project}/environments/{environment}/export",
                "/api/v1/projects/{project}/environments/{environment}/import",
                "/api/v1/projects/{project}/environments/{environment}/missing",
                "/api/v1/projects/{project}/environments/{environment}/missing/{name}/dismissal",
                "/api/v1/projects/{project}/environments/{environment}/purge",
                "/api/v1/projects/{project}/environments/{environment}/restore",
                "/api/v1/projects/{project}/environments/{environment}/secrets",
                "/api/v1/projects/{project}/environments/{environment}/secrets/{name}",
                "/api/v1/projects/{project}/environments/{environment}/secrets/{name}/access",
                "/api/v1/projects/{project}/environments/{environment}/secrets/{name}/purge",
                "/api/v1/projects/{project}/environments/{environment}/secrets/{name}/restore",
                "/api/v1/projects/{project}/environments/{environment}/secrets/{name}/rollback",
                "/api/v1/projects/{project}/environments/{environment}/secrets/{name}/versions",
                "/api/v1/projects/{project}/purge",
                "/api/v1/projects/{project}/restore",
                "/api/v1/sessions", "/api/v1/sessions/current",
                "/api/v1/tokens", "/api/v1/tokens/{id}",
                "/api/v1/users", "/api/v1/users/{id}/deactivate",
                "/api/v1/users/{id}/email", "/api/v1/users/{id}/password",
                "/api/v1/users/{id}/reactivate",
                "/problems", "/problems/{code}",
            ],
            paths);

        // The shapes are the ones docs/api.md names, spelled that way in the
        // components so that a generated client sees the specification's words.
        var schemas = document["components"]!["schemas"]!.AsObject()
            .Select(schema => schema.Key)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Handshake", schemas);
        Assert.Contains("Session", schemas);
        Assert.Contains("Token", schemas);
        Assert.Contains("Me", schemas);
        Assert.Contains("User", schemas);
        Assert.Contains("Invitation", schemas);
        Assert.Contains("Organization", schemas);
        Assert.Contains("Project", schemas);
        Assert.Contains("Environment", schemas);
        Assert.Contains("Secret", schemas);
        Assert.Contains("MissingKey", schemas);
        Assert.Contains("Access", schemas);
        Assert.Contains("Change", schemas);
        Assert.Contains("Version", schemas);
        Assert.Contains("ProblemDetails", schemas);

        // The browser page the device login needs is deliberately not in here:
        // this document is what a client is generated from, and that is a page
        // for a person (Specification §6.6).
        Assert.DoesNotContain("/device", document["paths"]!.AsObject().Select(path => path.Key));
    }

    [Fact]
    public async Task The_served_document_is_the_checked_in_one()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing(null);

        var served = JsonNode.Parse(await client.GetStringAsync(
            "/openapi/v1.json", TestContext.Current.CancellationToken))!;

        var path = Path.Combine(RepositoryRoot(), "docs", "api", "openapi.json");

        if (Environment.GetEnvironmentVariable(CaptureVariable) is "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path, Formatted(served), TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(path), $"{path} is missing; capture it with {CaptureVariable}=1.");

        var checkedIn = JsonNode.Parse(
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

        Assert.True(
            JsonNode.DeepEquals(served, checkedIn),
            "The instance serves a document other than docs/api/openapi.json. Regenerate it "
            + $"with {CaptureVariable}=1 and commit it with the change (ADR 0006).");
    }

    /// <summary>
    /// Two-space indent, one member per line, nothing escaped that need not be,
    /// a newline at the end — the same bytes CI's capture step produces for the
    /// same document, so that a local capture and CI's never disagree.
    /// </summary>
    private static string Formatted(JsonNode document) =>
        document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "Vaultaffe.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No Vaultaffe.slnx above the test binary.");
    }
}
