using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The version exchange of Specification §6.3, asked of a running instance: the
/// handshake a client reads before anything else, the header every answer
/// carries back, and the two ways a client is told it will not be served —
/// neither of which is a field it failed at.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class VersionExchangeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_handshake_names_the_product_the_release_and_the_versions_served()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing(null);

        var handshake = await client.GetFromJsonAsync<JsonNode>(
            "/api/handshake", TestContext.Current.CancellationToken);

        // A client pointed at the wrong host should find that out here, before
        // it decides anything about versions.
        Assert.Equal("vaultaffe", handshake!["product"]!.GetValue<string>());

        Assert.Equal("0.0.0-dev", handshake["release"]!.GetValue<string>());
        Assert.Equal(["v1"], handshake["apiVersions"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal("0.1.0", handshake["minimumClient"]!.GetValue<string>());
    }

    [Fact]
    public async Task Every_answer_carries_the_release_including_a_refused_one()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing("0.0.1");

        using var refused = await client.GetAsync(
            "/api/handshake", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UpgradeRequired, refused.StatusCode);
        Assert.Equal("0.0.0-dev", Assert.Single(refused.Headers.GetValues("Vaultaffe-Version")));
    }

    [Fact]
    public async Task A_client_older_than_the_floor_is_told_so_and_told_the_floor()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing("0.0.9");

        using var response = await client.GetAsync(
            "/api/handshake", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("client-too-old", problem["code"]!.GetValue<string>());
        Assert.Equal("/problems/client-too-old", problem["type"]!.GetValue<string>());
        Assert.Equal("0.1.0", problem["minimumClient"]!.GetValue<string>());
    }

    /// <summary>
    /// A working copy is not a release and has nothing to upgrade to. The CLI's
    /// own <c>Released()</c> is where that gets said out loud; the floor does not
    /// stand in front of a developer's build.
    /// </summary>
    [Theory]
    [InlineData("0.0.0-dev")]
    [InlineData("0.1.0")]
    [InlineData("v1.4.2")]
    [InlineData("2.0.0-rc.1")]
    public async Task A_client_at_or_above_the_floor_and_a_working_copy_are_served(string announced)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing(announced);

        using var response = await client.GetAsync(
            "/api/handshake", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_client_version_that_is_not_a_version_is_refused_as_that()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing("yesterday");

        using var response = await client.GetAsync(
            "/api/handshake", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("client-version-unreadable", problem["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_version_of_the_api_this_instance_does_not_serve_says_which_it_does()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing(null);

        using var response = await client.GetAsync(
            "/api/v2/secrets", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("unsupported-api-version", problem["code"]!.GetValue<string>());
        Assert.Equal(["v1"], problem["apiVersions"]!.AsArray().Select(v => v!.GetValue<string>()));
    }

    /// <summary>
    /// A path under a version this instance does serve is a missing endpoint and
    /// says that instead — the difference between "upgrade your server" and "you
    /// asked for something that never existed".
    /// </summary>
    [Fact]
    public async Task An_unknown_path_under_a_served_version_is_a_plain_not_found()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing(null);

        using var response = await client.GetAsync(
            "/api/v1/nothing-here", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("not-found", problem["code"]!.GetValue<string>());
    }
}
