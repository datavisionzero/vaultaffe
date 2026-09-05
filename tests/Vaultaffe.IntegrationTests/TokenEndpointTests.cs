using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// Token management (Specification §6.1, §6.4): create, name, revoke — the value
/// shown exactly once, and the whole of it human-only.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TokenEndpointTests(PostgresFixture postgres)
{
    /// <summary>
    /// The default of an agent token is the whole organization with every scope.
    /// The point is attribution, not restriction: an agent acts under its own
    /// token so the log can say an agent acted, and the human narrows it if they
    /// want to (§6.4).
    /// </summary>
    [Fact]
    public async Task An_agent_token_defaults_to_the_whole_organization_with_every_scope()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");

        var token = issued["token"]!;

        Assert.Equal("agent", token["kind"]!.GetValue<string>());
        Assert.True(token["reachesTheWholeOrganization"]!.GetValue<bool>());
        Assert.Equal(
            ["names", "read", "write", "delete"],
            token["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()));

        Assert.StartsWith(
            "vaultaffe_agent_", issued["value"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_service_token_defaults_to_names_and_read()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "service", "the deploy job");

        Assert.Equal(
            ["names", "read"],
            issued["token"]!["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()));
    }

    /// <summary>
    /// "May write but never read" is the case a read/read-write switch cannot
    /// express, and in a secrets manager reading is the dangerous half.
    /// </summary>
    [Fact]
    public async Task A_narrowed_token_carries_exactly_the_scopes_it_was_given()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var response = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "the writing agent", scopes = new[] { "names", "write" } },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var issued = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal(
            ["names", "write"],
            issued["token"]!["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()));
    }

    /// <summary>
    /// The value appears in the answer that created it and nowhere else, ever.
    /// A listing that carried one would be the product contradicting itself.
    /// </summary>
    [Fact]
    public async Task No_listing_carries_a_value()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");
        var value = issued["value"]!.GetValue<string>();

        using var listed = await human.GetAsync(
            "/api/v1/tokens", TestContext.Current.CancellationToken);

        var body = await listed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(value, body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\"", body, StringComparison.Ordinal);

        // Both of them are in it: the session the first run handed over, and the
        // agent token just made. A revocation list nobody can read is not one.
        var tokens = JsonNode.Parse(body)!.AsArray();

        Assert.Equal(
            ["agent", "session"],
            tokens.Select(token => token!["kind"]!.GetValue<string>()).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Token management is human-only: a token is itself a secret, and one an
    /// agent created through the CLI would be printed into its own context
    /// (§6.1). The refusal is <c>human-only</c> and not a bare <c>forbidden</c>,
    /// because the remedy is a different one — hand it to a person rather than
    /// ask for a wider token.
    /// </summary>
    [Fact]
    public async Task An_agent_may_not_create_a_token()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");

        using var agent = instance.ClientWith(issued["value"]!.GetValue<string>());

        using var response = await agent.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "a token of my own" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("human-only", problem["code"]!.GetValue<string>());
        Assert.Equal("create-token", problem["humanAction"]!.GetValue<string>());

        // Specification §8, scenario 5: the agent hands its human an action, and
        // the client names the command. A server that named one would name a
        // command of some release it cannot see (ADR 0010).
        Assert.DoesNotContain(
            "vaultaffe ",
            problem["detail"]!.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);

        // It can still read, which is the point: the restriction is on the one
        // action whose output is itself a secret, not on the agent.
        using var listed = await agent.GetAsync(
            "/api/v1/tokens", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
    }

    [Fact]
    public async Task An_agent_may_not_revoke_a_token_either()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");
        var id = issued["token"]!["id"]!.GetValue<string>();

        using var agent = instance.ClientWith(issued["value"]!.GetValue<string>());

        using var response = await agent.DeleteAsync(
            $"/api/v1/tokens/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("human-only", await IdentityTests.CodeOf(response));
    }

    /// <summary>
    /// A human-only endpoint is refused before it runs, which is what makes the
    /// enforcement one middleware rather than a check in a handler: the agent's
    /// token is never created, so nothing has to be undone.
    /// </summary>
    [Fact]
    public async Task A_refused_creation_creates_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");

        using var agent = instance.ClientWith(issued["value"]!.GetValue<string>());

        using var refused = await agent.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "a token of my own" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var listed = await human.GetAsync(
            "/api/v1/tokens", TestContext.Current.CancellationToken);

        var tokens = JsonNode.Parse(await listed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray();

        Assert.DoesNotContain(
            tokens, token => token!["name"]?.GetValue<string>() == "a token of my own");
    }

    /// <summary>
    /// A token an agent cannot create it also cannot create by not being
    /// authenticated at all: the human-only list is refused after authentication,
    /// not instead of it.
    /// </summary>
    [Fact]
    public async Task Nobody_at_all_is_still_unauthenticated_rather_than_human_only()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var nobody = instance.ClientWith(null);

        using var response = await nobody.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "a token from nowhere" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", await IdentityTests.CodeOf(response));
    }

    /// <summary>
    /// A session token comes out of signing in and nowhere else. One somebody
    /// could mint for another person is not a session.
    /// </summary>
    [Fact]
    public async Task A_session_token_cannot_be_created()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var response = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "session", name = "somebody else's session" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(response));
    }

    [Fact]
    public async Task A_token_needs_a_name_so_that_a_revocation_list_is_readable()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var response = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "   " },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(response));
    }

    [Fact]
    public async Task Revoking_stops_a_token_authenticating_and_keeps_its_row()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");
        var id = issued["token"]!["id"]!.GetValue<string>();

        using var agent = instance.ClientWith(issued["value"]!.GetValue<string>());

        using var before = await agent.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var revoked = await human.DeleteAsync(
            $"/api/v1/tokens/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        using var after = await agent.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // Revoked rather than deleted, so everything it ever signed in the change
        // log keeps an author (§6.5).
        using var listed = await human.GetAsync(
            "/api/v1/tokens", TestContext.Current.CancellationToken);

        var tokens = JsonNode.Parse(await listed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray();

        Assert.Contains(tokens, token => token!["id"]!.GetValue<string>() == id);
    }

    [Fact]
    public async Task Revoking_something_that_is_not_a_token_of_this_organization_is_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var response = await human.DeleteAsync(
            $"/api/v1/tokens/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<JsonNode> CreateAsync(HttpClient client, string kind, string name)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/tokens", new { kind, name }, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }
}
