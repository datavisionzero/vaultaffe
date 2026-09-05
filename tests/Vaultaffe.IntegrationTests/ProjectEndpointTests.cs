using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Vaultaffe.Domain.History;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// Projects and environments (Specification §5, §6.5): create, list, rename,
/// delete recoverably, restore — and the change log that goes with every one of
/// them, because it belongs to the first write path rather than to a later
/// ticket.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ProjectEndpointTests(PostgresFixture postgres)
{
    /// <summary>
    /// A project with no environments is a project nothing can be written into.
    /// The three are a default rather than a rule (§5).
    /// </summary>
    [Fact]
    public async Task A_project_is_created_with_dev_staging_and_prod()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var project = await CreateAsync(human, "webshop-api");

        Assert.Equal("webshop-api", project["name"]!.GetValue<string>());
        Assert.Equal(
            ["dev", "prod", "staging"],
            Names(project["environments"]!).Order(StringComparer.Ordinal));
        Assert.Null(project["deletedAt"]);
    }

    [Fact]
    public async Task Environments_can_be_named_at_creation_instead()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var response = await human.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "landing-page", environments = new[] { "preview", "live" } },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var project = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal(
            ["live", "preview"], Names(project["environments"]!).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// ADR 0003: a project's name appears inside a <c>vaultaffe://</c> reference,
    /// so it stays narrow. The refusal is a validation failure and says the rule.
    /// </summary>
    [Theory]
    [InlineData("Webshop API")]
    [InlineData("WEBSHOP")]
    [InlineData("-leading")]
    [InlineData("")]
    public async Task A_name_that_could_not_appear_in_a_reference_is_refused(string name)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var response = await human.PostAsJsonAsync(
            "/api/v1/projects", new { name }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(response));
    }

    [Fact]
    public async Task A_name_in_use_is_a_conflict_and_not_a_second_project()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await CreateAsync(human, "webshop-api");

        using var again = await human.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "webshop-api" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var problem = await ProblemOf(again);

        Assert.Equal("name-taken", problem["code"]!.GetValue<string>());
        Assert.False(problem["takenBySomethingDeleted"]!.GetValue<bool>());
    }

    /// <summary>
    /// A deleted project keeps its name for exactly as long as it is
    /// recoverable, so recreating it cannot silently replace its predecessor
    /// (§6.5). The refusal says which of the two cases this is, because "I
    /// deleted it, why can I not recreate it" is the question it exists to
    /// answer.
    /// </summary>
    [Fact]
    public async Task A_deleted_project_keeps_its_name_reserved()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await CreateAsync(human, "webshop-api");

        using var deleted = await human.DeleteAsync(
            "/api/v1/projects/webshop-api", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        using var again = await human.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "webshop-api" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var problem = await ProblemOf(again);

        Assert.Equal("name-taken", problem["code"]!.GetValue<string>());
        Assert.True(problem["takenBySomethingDeleted"]!.GetValue<bool>());
    }

    /// <summary>
    /// Deleted is out of listings and use, and restoring brings it back. The
    /// environments come back with it, because deleting a project never touched
    /// them: the subtree is retained and restored as one.
    /// </summary>
    [Fact]
    public async Task Deleting_is_recoverable_and_the_subtree_comes_back_with_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await CreateAsync(human, "webshop-api");

        using var deleted = await human.DeleteAsync(
            "/api/v1/projects/webshop-api", TestContext.Current.CancellationToken);

        deleted.EnsureSuccessStatusCode();

        Assert.Empty((await ListAsync(human)).AsArray());

        using var gone = await human.GetAsync(
            "/api/v1/projects/webshop-api", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        using var restored = await human.PostAsync(
            "/api/v1/projects/webshop-api/restore", null, TestContext.Current.CancellationToken);

        restored.EnsureSuccessStatusCode();

        var project = JsonNode.Parse(await restored.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Null(project["deletedAt"]);
        Assert.Equal(
            ["dev", "prod", "staging"],
            Names(project["environments"]!).Order(StringComparer.Ordinal));
        Assert.Single((await ListAsync(human)).AsArray());
    }

    /// <summary>
    /// An environment deleted before its project was stays deleted when the
    /// project comes back. That is what "restored as one" means: the subtree
    /// returns as it was, not as it would have been.
    /// </summary>
    [Fact]
    public async Task Restoring_a_project_does_not_undelete_what_was_deleted_before_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await CreateAsync(human, "webshop-api");

        using var environmentGone = await human.DeleteAsync(
            "/api/v1/projects/webshop-api/environments/staging",
            TestContext.Current.CancellationToken);

        environmentGone.EnsureSuccessStatusCode();

        using var projectGone = await human.DeleteAsync(
            "/api/v1/projects/webshop-api", TestContext.Current.CancellationToken);

        projectGone.EnsureSuccessStatusCode();

        using var restored = await human.PostAsync(
            "/api/v1/projects/webshop-api/restore", null, TestContext.Current.CancellationToken);

        restored.EnsureSuccessStatusCode();

        var project = JsonNode.Parse(await restored.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal(
            ["dev", "prod"], Names(project["environments"]!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_environment_is_added_renamed_and_deleted_on_its_own()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await CreateAsync(human, "webshop-api");

        using var created = await human.PostAsJsonAsync(
            "/api/v1/projects/webshop-api/environments",
            new { name = "dev-someone" },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        using var renamed = await human.PatchAsJsonAsync(
            "/api/v1/projects/webshop-api/environments/dev-someone",
            new { name = "sandbox" },
            TestContext.Current.CancellationToken);

        renamed.EnsureSuccessStatusCode();

        using var listed = await human.GetAsync(
            "/api/v1/projects/webshop-api/environments", TestContext.Current.CancellationToken);

        var environments = JsonNode.Parse(await listed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal(
            ["dev", "prod", "sandbox", "staging"],
            Names(environments).Order(StringComparer.Ordinal));

        using var deleted = await human.DeleteAsync(
            "/api/v1/projects/webshop-api/environments/sandbox",
            TestContext.Current.CancellationToken);

        deleted.EnsureSuccessStatusCode();

        using var after = await human.GetAsync(
            "/api/v1/projects/webshop-api/environments", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            "sandbox",
            Names(JsonNode.Parse(await after.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken))!));
    }

    [Fact]
    public async Task Renaming_a_project_keeps_its_id_and_its_environments()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var created = await CreateAsync(human, "webshop-api");

        using var renamed = await human.PatchAsJsonAsync(
            "/api/v1/projects/webshop-api",
            new { name = "shop-api" },
            TestContext.Current.CancellationToken);

        renamed.EnsureSuccessStatusCode();

        var project = JsonNode.Parse(await renamed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal(created["id"]!.GetValue<string>(), project["id"]!.GetValue<string>());
        Assert.Equal("shop-api", project["name"]!.GetValue<string>());
        Assert.Equal(3, project["environments"]!.AsArray().Count);
    }

    /// <summary>
    /// Specification §6.5: the change log belongs to the first write path rather
    /// than to a later ticket, and every mutation is in it with the identity's
    /// type beside it. Four creations here — the project and its three
    /// environments — then the deletion and the restore.
    /// </summary>
    [Fact]
    public async Task Every_change_is_in_the_log_with_the_identity_that_made_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await CreateAsync(human, "webshop-api");

        using var deleted = await human.DeleteAsync(
            "/api/v1/projects/webshop-api", TestContext.Current.CancellationToken);

        deleted.EnsureSuccessStatusCode();

        using var restored = await human.PostAsync(
            "/api/v1/projects/webshop-api/restore", null, TestContext.Current.CancellationToken);

        restored.EnsureSuccessStatusCode();

        var entries = await LogAsync(instance, human);

        Assert.Equal(
            [
                ChangeAction.Created, ChangeAction.Created, ChangeAction.Created,
                ChangeAction.Created, ChangeAction.Deleted, ChangeAction.Restored,
            ],
            entries.Select(entry => entry.Action));

        Assert.All(entries, entry => Assert.Equal("webshop-api", entry.ProjectName));
        Assert.All(entries, entry => Assert.Equal(IdentityType.HumanSession, entry.IdentityType));
        Assert.All(entries, entry => Assert.Equal("Maintainer", entry.IdentityName));
    }

    /// <summary>
    /// An agent acts under its own token so the log says an agent acted, rather
    /// than the person whose terminal it sits in (§6.4). That is the whole reason
    /// the kind exists on day one.
    /// </summary>
    [Fact]
    public async Task An_agent_appears_in_the_log_as_an_agent()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var issued = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "quiet-otter-42" },
            TestContext.Current.CancellationToken);

        issued.EnsureSuccessStatusCode();

        var token = JsonNode.Parse(await issued.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]!.GetValue<string>();

        using var agent = instance.ClientWith(token);

        await CreateAsync(agent, "webshop-api");

        var entries = await LogAsync(instance, human);

        Assert.All(entries, entry => Assert.Equal(IdentityType.AgentToken, entry.IdentityType));
        Assert.All(entries, entry => Assert.Equal("quiet-otter-42", entry.IdentityName));
    }

    /// <summary>
    /// A refused act writes nothing, log included: the entry is enlisted and
    /// committed by the same save as the change it describes, so there is no
    /// window in which the log says something the data does not.
    /// </summary>
    [Fact]
    public async Task A_refused_change_leaves_no_entry_behind()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await CreateAsync(human, "webshop-api");

        using var refused = await human.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "webshop-api" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // Four from the one project that was created, and nothing from the one
        // that was not.
        Assert.Equal(4, (await LogAsync(instance, human)).Count);
    }

    /// <summary>
    /// Creating a project needs a token that reaches the whole organization: a
    /// bound token is narrowed to projects that exist, and a new one is by
    /// definition not among them.
    /// </summary>
    [Fact]
    public async Task A_bound_token_sees_its_project_and_cannot_create_another()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var mine = await CreateAsync(human, "webshop-api");
        await CreateAsync(human, "landing-page");

        using var issued = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new
            {
                kind = "agent",
                name = "the agent on the shop",
                bindings = new[] { new { projectId = mine["id"]!.GetValue<string>() } },
            },
            TestContext.Current.CancellationToken);

        issued.EnsureSuccessStatusCode();

        using var bound = instance.ClientWith(JsonNode.Parse(
            await issued.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken))!["value"]!.GetValue<string>());

        // The listing is narrowed rather than refused: being told "forbidden" for
        // the projects it is not bound to would be a listing nobody could use.
        Assert.Equal(["webshop-api"], Names(await ListAsync(bound)));

        using var elsewhere = await bound.GetAsync(
            "/api/v1/projects/landing-page", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.Equal("out-of-reach", await IdentityTests.CodeOf(elsewhere));

        using var created = await bound.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "something-new" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal("out-of-reach", await IdentityTests.CodeOf(created));

        // Inside its own project it is an ordinary caller.
        using var environment = await bound.PostAsJsonAsync(
            "/api/v1/projects/webshop-api/environments",
            new { name = "sandbox" },
            TestContext.Current.CancellationToken);

        environment.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A token bound to one environment reaches into the project — it can list
    /// what it may see — and must not be able to make the environment beside the
    /// one its binding named, or the binding would be a suggestion.
    /// </summary>
    [Fact]
    public async Task A_token_bound_to_one_environment_sees_only_that_one()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var project = await CreateAsync(human, "webshop-api");

        var staging = project["environments"]!.AsArray()
            .Single(one => one!["name"]!.GetValue<string>() == "staging")!["id"]!.GetValue<string>();

        using var issued = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new
            {
                kind = "service",
                name = "the staging deploy",
                scopes = new[] { "names", "read", "write", "delete" },
                bindings = new[]
                {
                    new { projectId = project["id"]!.GetValue<string>(), environmentId = staging },
                },
            },
            TestContext.Current.CancellationToken);

        issued.EnsureSuccessStatusCode();

        using var bound = instance.ClientWith(JsonNode.Parse(
            await issued.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken))!["value"]!.GetValue<string>());

        using var listed = await bound.GetAsync(
            "/api/v1/projects/webshop-api/environments", TestContext.Current.CancellationToken);

        listed.EnsureSuccessStatusCode();

        Assert.Equal(
            ["staging"],
            Names(JsonNode.Parse(await listed.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken))!));

        using var beside = await bound.PostAsJsonAsync(
            "/api/v1/projects/webshop-api/environments",
            new { name = "sandbox" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, beside.StatusCode);
        Assert.Equal("out-of-reach", await IdentityTests.CodeOf(beside));

        using var deletedProject = await bound.DeleteAsync(
            "/api/v1/projects/webshop-api", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, deletedProject.StatusCode);
    }

    /// <summary>
    /// A service token defaults to names and read, and the scope a change needs
    /// is declared on the endpoint. The refusal says which scope was missing.
    /// </summary>
    [Fact]
    public async Task A_token_without_write_may_look_and_not_touch()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await CreateAsync(human, "webshop-api");

        using var issued = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "service", name = "the deploy job" },
            TestContext.Current.CancellationToken);

        issued.EnsureSuccessStatusCode();

        using var reader = instance.ClientWith(JsonNode.Parse(
            await issued.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken))!["value"]!.GetValue<string>());

        Assert.Equal(["webshop-api"], Names(await ListAsync(reader)));

        using var created = await reader.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "something-new" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);

        var problem = await ProblemOf(created);

        Assert.Equal("insufficient-scope", problem["code"]!.GetValue<string>());
        Assert.Equal(
            ["write"],
            problem["requiredScopes"]!.AsArray().Select(scope => scope!.GetValue<string>()));

        using var deleted = await reader.DeleteAsync(
            "/api/v1/projects/webshop-api", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);
        Assert.Equal("insufficient-scope", await IdentityTests.CodeOf(deleted));
    }

    /// <summary>
    /// The window is 72 hours (§6.5), and past it there is nothing to restore.
    /// The row is still there — it is removed by a purge or by the sweep that
    /// reads the deadline — so this is a refusal and not a "no such project".
    /// </summary>
    [Fact]
    public async Task Past_its_window_there_is_nothing_left_to_restore()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await CreateAsync(human, "webshop-api");

        using var deleted = await human.DeleteAsync(
            "/api/v1/projects/webshop-api", TestContext.Current.CancellationToken);

        deleted.EnsureSuccessStatusCode();

        instance.Clock.MoveOn(TimeSpan.FromHours(73));

        using var refused = await human.PostAsync(
            "/api/v1/projects/webshop-api/restore", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Gone, refused.StatusCode);
        Assert.Equal("not-recoverable", await IdentityTests.CodeOf(refused));

        // And the name is still not free, because the row is still there. What
        // frees it is a purge or the sweep, and both are their own ticket.
        using var again = await human.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "webshop-api" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Restoring_something_that_was_never_deleted_is_uneventful()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await CreateAsync(human, "webshop-api");

        using var restored = await human.PostAsync(
            "/api/v1/projects/webshop-api/restore", null, TestContext.Current.CancellationToken);

        restored.EnsureSuccessStatusCode();

        // Four entries from the creation, and no fifth: nothing happened, so
        // nothing is in the log.
        Assert.Equal(4, (await LogAsync(instance, human)).Count);
    }

    [Fact]
    public async Task Nothing_here_answers_a_request_with_no_token()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var nobody = instance.ClientWith(null);

        using var response = await nobody.GetAsync(
            "/api/v1/projects", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<JsonNode> CreateAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/projects", new { name }, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonNode> ListAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/api/v1/projects", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonNode> ProblemOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

    private static IEnumerable<string> Names(JsonNode listing) =>
        listing.AsArray().Select(one => one!["name"]!.GetValue<string>());

    /// <summary>
    /// What this instance recorded, oldest first. There is no endpoint for the
    /// change log yet — it arrives with its own ticket — so this reads the rows
    /// the way an operator would.
    /// </summary>
    private static async Task<IReadOnlyList<ChangeLogEntry>> LogAsync(
        AnInstance instance, HttpClient client)
    {
        using var me = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        me.EnsureSuccessStatusCode();

        var organizationId = JsonNode.Parse(await me.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["organizationId"]!.GetValue<Guid>();

        await using var context = Migrated.ReaderOn(instance.ConnectionString, organizationId);

        return await context.ChangeLog
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Action)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
