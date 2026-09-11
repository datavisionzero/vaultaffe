using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// Token management (Specification §6.1, §6.4): create, name, change, rotate,
/// revoke and purge — a value shown exactly once, and the whole of it human-only
/// but for the one act an agent may aim at itself (ADR 0022).
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
        // log keeps an author (§6.5). Out of the default listing and not out of
        // the record: asking for the revoked ones finds it exactly where it was.
        using var listed = await human.GetAsync(
            "/api/v1/tokens?revoked=true", TestContext.Current.CancellationToken);

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

    /// <summary>
    /// The act that exists because the value cannot change. A token that has to
    /// reach one more project is amended rather than reissued: the same string
    /// keeps authenticating, so nothing holding it has to be visited, and what
    /// changed is what this instance lets it through for.
    /// </summary>
    [Fact]
    public async Task Changing_a_token_widens_it_without_touching_its_value()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var project = await human.PostAsJsonAsync(
            "/api/v1/projects", new { name = "webshop-api" }, TestContext.Current.CancellationToken);

        project.EnsureSuccessStatusCode();

        var catalogue = JsonNode.Parse(await project.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        using var created = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "the agent in this terminal", scopes = new[] { "names" } },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        var issued = JsonNode.Parse(await created.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        var id = issued["token"]!["id"]!.GetValue<string>();
        var value = issued["value"]!.GetValue<string>();

        using var agent = instance.ClientWith(value);

        using var changed = await human.PatchAsJsonAsync(
            $"/api/v1/tokens/{id}",
            new
            {
                name = "the agent on the webshop",
                scopes = new[] { "names", "read" },
                bindings = new[] { new { projectId = catalogue["id"]!.GetValue<string>() } },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var after = JsonNode.Parse(await changed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("the agent on the webshop", after["name"]!.GetValue<string>());
        Assert.Equal(
            ["names", "read"],
            after["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()));
        Assert.False(after["reachesTheWholeOrganization"]!.GetValue<bool>());
        Assert.Single(after["bindings"]!.AsArray());

        // The value never appears again, not even in the answer that changed
        // what it may do.
        Assert.DoesNotContain(
            "\"value\"",
            await changed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        // And the same string still authenticates, carrying what it was just
        // given: nobody had to go round the machines holding it.
        using var me = await agent.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var caller = JsonNode.Parse(await me.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("the agent on the webshop", caller["tokenName"]!.GetValue<string>());
        Assert.Equal(
            ["names", "read"],
            caller["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()));
    }

    /// <summary>
    /// Omitted is unchanged, which is what makes a rename a rename. The reach is
    /// the one field where an empty list means something of its own — the whole
    /// organization — and that only works because omitting it says nothing.
    /// </summary>
    [Fact]
    public async Task Changing_only_the_name_leaves_the_scopes_and_the_reach_alone()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "service", "ci");
        var id = issued["token"]!["id"]!.GetValue<string>();

        using var changed = await human.PatchAsJsonAsync(
            $"/api/v1/tokens/{id}",
            new { name = "the deploy job" },
            TestContext.Current.CancellationToken);

        changed.EnsureSuccessStatusCode();

        var after = JsonNode.Parse(await changed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("the deploy job", after["name"]!.GetValue<string>());
        Assert.Equal(
            ["names", "read"],
            after["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()));
        Assert.True(after["reachesTheWholeOrganization"]!.GetValue<bool>());
    }

    /// <summary>
    /// And back out again. An empty list is the deliberate request for the whole
    /// organization, which is the one place where "nothing" is an answer rather
    /// than a silence — omitting the field leaves the reach alone.
    /// </summary>
    [Fact]
    public async Task An_empty_reach_is_the_whole_organization_and_takes_the_bindings_off()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var project = await human.PostAsJsonAsync(
            "/api/v1/projects", new { name = "webshop-api" }, TestContext.Current.CancellationToken);

        project.EnsureSuccessStatusCode();

        var id = JsonNode.Parse(await project.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["id"]!.GetValue<string>();

        using var created = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new
            {
                kind = "service",
                name = "ci",
                bindings = new[] { new { projectId = id } },
            },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        var bound = JsonNode.Parse(await created.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["token"]!["id"]!.GetValue<string>();

        using var changed = await human.PatchAsJsonAsync(
            $"/api/v1/tokens/{bound}",
            new { bindings = Array.Empty<object>() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var after = JsonNode.Parse(await changed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.True(after["reachesTheWholeOrganization"]!.GetValue<bool>());
        Assert.Empty(after["bindings"]!.AsArray());

        // And it survived the round trip: the rows are gone rather than hidden.
        using var listed = await human.GetAsync(
            "/api/v1/tokens", TestContext.Current.CancellationToken);

        var row = JsonNode.Parse(await listed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray()
            .Single(token => token!["id"]!.GetValue<string>() == bound)!;

        Assert.Empty(row["bindings"]!.AsArray());
    }

    /// <summary>
    /// Human-only for the mirror image of the reason revoking is: an agent that
    /// could widen a token could widen its own.
    /// </summary>
    [Fact]
    public async Task An_agent_may_not_change_a_token()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");
        var id = issued["token"]!["id"]!.GetValue<string>();

        using var agent = instance.ClientWith(issued["value"]!.GetValue<string>());

        using var response = await agent.PatchAsJsonAsync(
            $"/api/v1/tokens/{id}",
            new { scopes = new[] { "names", "read", "write", "delete" } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("human-only", await IdentityTests.CodeOf(response));

        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("change-token", problem["humanAction"]!.GetValue<string>());
    }

    /// <summary>
    /// A session is not a thing anybody named or bound, and a revoked token
    /// reaches nothing to arrange. Both are refused as validation rather than as
    /// nothing found: the row is there, and this is not what it is for.
    /// </summary>
    [Fact]
    public async Task A_session_and_a_revoked_token_are_not_changed()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        var session = await instance.StartAsync();
        using var human = instance.ClientWith(session);

        using var listed = await human.GetAsync(
            "/api/v1/tokens", TestContext.Current.CancellationToken);

        var mine = JsonNode.Parse(await listed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray()
            .Single(token => token!["kind"]!.GetValue<string>() == "session")!["id"]!
            .GetValue<string>();

        using var refusedSession = await human.PatchAsJsonAsync(
            $"/api/v1/tokens/{mine}",
            new { name = "my laptop" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refusedSession.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(refusedSession));

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");
        var id = issued["token"]!["id"]!.GetValue<string>();

        using var revoked = await human.DeleteAsync(
            $"/api/v1/tokens/{id}", TestContext.Current.CancellationToken);

        revoked.EnsureSuccessStatusCode();

        using var refusedRevoked = await human.PatchAsJsonAsync(
            $"/api/v1/tokens/{id}",
            new { name = "the agent that was" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refusedRevoked.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(refusedRevoked));
    }

    /// <summary>
    /// A purge is the second half of a revocation and not a quieter one: a row
    /// that vanished while its value still worked would be a credential nobody
    /// could find and nobody could take back.
    /// </summary>
    [Fact]
    public async Task A_token_that_still_works_is_not_purged()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");
        var id = issued["token"]!["id"]!.GetValue<string>();

        using var response = await human.PostAsync(
            $"/api/v1/tokens/{id}/purge", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(response));
    }

    /// <summary>
    /// And what a purge does: the row goes, and everything the token ever did
    /// stays in the change log under its name. That is what the log records
    /// identities by name for — it outlives the rows it talks about.
    /// </summary>
    [Fact]
    public async Task Purging_a_revoked_token_removes_the_row_and_keeps_the_history()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent that was");
        var id = issued["token"]!["id"]!.GetValue<string>();

        using var revoked = await human.DeleteAsync(
            $"/api/v1/tokens/{id}", TestContext.Current.CancellationToken);

        revoked.EnsureSuccessStatusCode();

        using var purged = await human.PostAsync(
            $"/api/v1/tokens/{id}/purge", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, purged.StatusCode);

        // The answer is the row as it last was, because this is the only place it
        // exists from here on — and it carries no value, the way no listing ever
        // has.
        var gone = JsonNode.Parse(await purged.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("the agent that was", gone["name"]!.GetValue<string>());

        using var listed = await human.GetAsync(
            "/api/v1/tokens", TestContext.Current.CancellationToken);

        var tokens = JsonNode.Parse(await listed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray();

        Assert.DoesNotContain(tokens, token => token!["id"]!.GetValue<string>() == id);

        // Created, revoked, purged — three entries under a name that no longer
        // belongs to any row.
        using var changes = await human.GetAsync(
            "/api/v1/changes", TestContext.Current.CancellationToken);

        var entries = JsonNode.Parse(await changes.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["entries"]!.AsArray()
            .Where(entry => entry!["about"]?.GetValue<string>() == "the agent that was")
            .Select(entry => entry!["action"]!.GetValue<string>())
            .ToList();

        Assert.Equal(
            ["token-created", "token-purged", "token-revoked"],
            entries.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_agent_may_not_purge_a_token()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");

        using var agent = instance.ClientWith(issued["value"]!.GetValue<string>());

        using var response = await agent.PostAsync(
            $"/api/v1/tokens/{issued["token"]!["id"]!.GetValue<string>()}/purge",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("human-only", await IdentityTests.CodeOf(response));

        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("purge-token", problem["humanAction"]!.GetValue<string>());
    }

    /// <summary>
    /// The listing answers what still authenticates. The revoked row is not gone
    /// — it never will be, because everything it signed keeps an author that way
    /// — it is simply not what somebody opening a credential list came to see.
    /// </summary>
    [Fact]
    public async Task A_listing_leaves_out_the_revoked_until_they_are_asked_for()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent that was");
        var id = issued["token"]!["id"]!.GetValue<string>();

        using var revoked = await human.DeleteAsync(
            $"/api/v1/tokens/{id}", TestContext.Current.CancellationToken);

        revoked.EnsureSuccessStatusCode();

        Assert.DoesNotContain(await ListAsync(human, revoked: false), row => Id(row) == id);
        Assert.Contains(await ListAsync(human, revoked: true), row => Id(row) == id);
    }

    /// <summary>
    /// What rotation is: the same credential under another value. The name, the
    /// scopes and the reach come across untouched, the row that was is revoked
    /// rather than overwritten — so the date the value in circulation changed is
    /// a fact the instance holds — and the successor is a row of its own.
    /// </summary>
    [Fact]
    public async Task Rotating_issues_the_next_value_and_kills_the_one_it_replaces()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var project = await human.PostAsJsonAsync(
            "/api/v1/projects", new { name = "webshop-api" }, TestContext.Current.CancellationToken);

        project.EnsureSuccessStatusCode();

        var catalogue = JsonNode.Parse(await project.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        using var created = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new
            {
                kind = "agent",
                name = "the agent on the webshop",
                scopes = new[] { "names", "read" },
                bindings = new[] { new { projectId = catalogue["id"]!.GetValue<string>() } },
            },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        var issued = JsonNode.Parse(await created.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        var id = issued["token"]!["id"]!.GetValue<string>();
        var was = issued["value"]!.GetValue<string>();

        using var rotated = await human.PostAsync(
            $"/api/v1/tokens/{id}/rotate", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        var next = JsonNode.Parse(await rotated.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        var successor = next["token"]!;

        Assert.NotEqual(id, successor["id"]!.GetValue<string>());
        Assert.Equal("the agent on the webshop", successor["name"]!.GetValue<string>());
        Assert.Equal(
            ["names", "read"],
            successor["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()));
        Assert.Single(successor["bindings"]!.AsArray());
        Assert.Null(successor["revokedAt"]);

        // The value is a value, and it is not the one it replaces.
        var now = next["value"]!.GetValue<string>();

        Assert.StartsWith("vaultaffe_agent_", now, StringComparison.Ordinal);
        Assert.NotEqual(was, now);

        // The old one is dead where it stands, with no overlap: a rotation is
        // usually the answer to something having gone wrong.
        using var stale = instance.ClientWith(was);
        using var refused = await stale.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using var fresh = instance.ClientWith(now);
        using var admitted = await fresh.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);

        // Both rows are there, and only one of them works. The old one is out of
        // the way rather than out of the record.
        var working = await ListAsync(human, revoked: false);

        Assert.DoesNotContain(working, row => Id(row) == id);
        Assert.Contains(working, row => Id(row) == successor["id"]!.GetValue<string>());

        Assert.Contains(await ListAsync(human, revoked: true), row => Id(row) == id);

        // One entry for one act, under the name that continues.
        using var changes = await human.GetAsync(
            "/api/v1/changes", TestContext.Current.CancellationToken);

        var entries = JsonNode.Parse(await changes.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["entries"]!.AsArray()
            .Where(entry => entry!["about"]?.GetValue<string>() == "the agent on the webshop")
            .Select(entry => entry!["action"]!.GetValue<string>())
            .ToList();

        Assert.Equal(["token-created", "token-rotated"], entries.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A token issued to run for a while is renewed for that while again, counted
    /// from now. What a person agreed to is the length, and nothing here
    /// lengthens it.
    /// </summary>
    [Fact]
    public async Task Rotating_gives_back_the_expiry_it_was_given_the_first_time()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var runs = DateTimeOffset.UtcNow.AddDays(30);

        using var created = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "service", name = "the deploy job", expiresAt = runs },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        var issued = JsonNode.Parse(await created.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["token"]!;

        using var rotated = await human.PostAsync(
            $"/api/v1/tokens/{issued["id"]!.GetValue<string>()}/rotate",
            null,
            TestContext.Current.CancellationToken);

        rotated.EnsureSuccessStatusCode();

        var successor = JsonNode.Parse(await rotated.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["token"]!;

        var before = issued["expiresAt"]!.GetValue<DateTimeOffset>();
        var after = successor["expiresAt"]!.GetValue<DateTimeOffset>();

        // The same thirty days, begun again: never earlier than the expiry it
        // replaces, and never a day longer than the length it was given.
        Assert.True(after >= before);
        Assert.True(after - before < TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// The exception the human-only list has, and the whole of it: an agent
    /// replacing the value of the token it is itself holding gains nothing it did
    /// not already have (ADR 0022).
    /// </summary>
    [Fact]
    public async Task An_agent_rotates_the_token_it_is_holding()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent in this terminal");
        var id = issued["token"]!["id"]!.GetValue<string>();

        using var agent = instance.ClientWith(issued["value"]!.GetValue<string>());

        using var rotated = await agent.PostAsync(
            $"/api/v1/tokens/{id}/rotate", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        var next = JsonNode.Parse(await rotated.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        using var carrying = instance.ClientWith(next["value"]!.GetValue<string>());
        using var admitted = await carrying.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);

        // Still the same agent to the log, and still accountable to the same
        // person: a rotation is a value, not a new identity.
        var me = JsonNode.Parse(await admitted.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("the agent in this terminal", me["tokenName"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_agent_may_not_rotate_a_token_that_is_not_its_own()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var mine = await CreateAsync(human, "agent", "the agent in this terminal");
        var theirs = await CreateAsync(human, "agent", "the agent in the other terminal");

        using var agent = instance.ClientWith(mine["value"]!.GetValue<string>());

        using var response = await agent.PostAsync(
            $"/api/v1/tokens/{theirs["token"]!["id"]!.GetValue<string>()}/rotate",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("human-only", await IdentityTests.CodeOf(response));

        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("rotate-token", problem["humanAction"]!.GetValue<string>());
    }

    /// <summary>
    /// And a service token not even its own: its value lives in a pipeline's
    /// secret store or a deployment's environment, neither of which this instance
    /// can write to, so rotating itself would replace a credential that works
    /// with one nothing is holding.
    /// </summary>
    [Fact]
    public async Task A_service_token_does_not_rotate_itself()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "service", "the deploy job");

        using var service = instance.ClientWith(issued["value"]!.GetValue<string>());

        using var response = await service.PostAsync(
            $"/api/v1/tokens/{issued["token"]!["id"]!.GetValue<string>()}/rotate",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("human-only", await IdentityTests.CodeOf(response));
    }

    /// <summary>
    /// A session is what signing in left behind, and signing in again is how
    /// another one is got.
    /// </summary>
    [Fact]
    public async Task A_session_is_not_rotated()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);

        var session = await instance.StartAsync();
        using var human = instance.ClientWith(session);

        var mine = (await ListAsync(human, revoked: false))
            .Single(row => row!["kind"]!.GetValue<string>() == "session");

        using var response = await human.PostAsync(
            $"/api/v1/tokens/{Id(mine)}/rotate", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(response));
    }

    /// <summary>
    /// Putting a withdrawn credential back is creating one, and that is its own
    /// act — under a name the revocation list is still showing.
    /// </summary>
    [Fact]
    public async Task A_revoked_token_is_not_rotated()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var issued = await CreateAsync(human, "agent", "the agent that was");
        var id = issued["token"]!["id"]!.GetValue<string>();

        using var revoked = await human.DeleteAsync(
            $"/api/v1/tokens/{id}", TestContext.Current.CancellationToken);

        revoked.EnsureSuccessStatusCode();

        using var response = await human.PostAsync(
            $"/api/v1/tokens/{id}/rotate", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(response));
    }

    private static async Task<JsonArray> ListAsync(HttpClient client, bool revoked)
    {
        using var response = await client.GetAsync(
            revoked ? "/api/v1/tokens?revoked=true" : "/api/v1/tokens",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray();
    }

    private static string Id(JsonNode? row) => row!["id"]!.GetValue<string>();

    private static async Task<JsonNode> CreateAsync(HttpClient client, string kind, string name)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/tokens", new { kind, name }, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }
}
