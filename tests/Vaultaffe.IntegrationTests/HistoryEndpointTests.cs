using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The change log and the value history (Specification §6.5) — two things that
/// get lumped together and have completely different risk profiles. One holds no
/// value at all; the other holds a few under tight bounds and hands none of them
/// out.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class HistoryEndpointTests(PostgresFixture postgres)
{
    private const string Secrets = "/api/v1/projects/webshop-api/environments/dev/secrets";

    /// <summary>
    /// What the log is for: an agent wrote this, and the log says so — rather
    /// than saying the name of the person whose terminal it sits in.
    /// </summary>
    [Fact]
    public async Task The_log_names_the_identity_and_its_type()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);
        using var agent = instance.ClientWith(await AgentAsync(human, "quiet-otter-42"));

        using var written = await agent.PutAsJsonAsync(
            $"{Secrets}/STRIPE_KEY",
            new { value = "not-a-key-at-all" },
            TestContext.Current.CancellationToken);

        written.EnsureSuccessStatusCode();

        var entries = await ChangesAsync(human, "?project=webshop-api&environment=dev&secret=STRIPE_KEY");

        Assert.Equal(
            ["value-set", "created"],
            entries.Select(entry => entry!["action"]!.GetValue<string>()));

        Assert.All(entries, entry =>
        {
            Assert.Equal("agent-token", entry!["identity"]!["type"]!.GetValue<string>());
            Assert.Equal("quiet-otter-42", entry["identity"]!["name"]!.GetValue<string>());
            Assert.Equal("webshop-api", entry["project"]!.GetValue<string>());
            Assert.Equal("dev", entry["environment"]!.GetValue<string>());
        });
    }

    /// <summary>
    /// No value, not even as a diff. Doppler put the old and the new one into the
    /// log and consequently had to bolt on a redaction that does not truly
    /// delete; we follow AWS instead, and this is what keeps that where it is.
    /// </summary>
    [Fact]
    public async Task No_value_reaches_the_log_however_many_times_one_is_written()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the-first-one-nobody-should-see");

        using var replaced = await human.PutAsJsonAsync(
            $"{Secrets}/STRIPE_KEY",
            new { value = "the-second-one-nobody-should-see", replace = true },
            TestContext.Current.CancellationToken);

        replaced.EnsureSuccessStatusCode();

        using var response = await human.GetAsync(
            "/api/v1/changes", TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("nobody-should-see", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\"", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A read is not a change. <c>run</c> reads values, but a successful read
    /// does not prove an application started, and the MVP does not pretend to an
    /// execution history.
    /// </summary>
    [Fact]
    public async Task Reading_a_value_is_not_in_the_log()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-at-all");

        var before = (await PageAsync(human, "?project=webshop-api"))["total"]!.GetValue<int>();

        using var read = await human.GetAsync(
            $"{Secrets}/STRIPE_KEY", TestContext.Current.CancellationToken);

        read.EnsureSuccessStatusCode();

        using var exported = await human.GetAsync(
            "/api/v1/projects/webshop-api/environments/dev/export",
            TestContext.Current.CancellationToken);

        exported.EnsureSuccessStatusCode();

        Assert.Equal(
            before, (await PageAsync(human, "?project=webshop-api"))["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_page_is_a_limit_and_an_offset_over_one_stable_order()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        for (var key = 0; key < 6; key++)
        {
            await SetAsync(human, $"KEY_{key}", $"value {key}");
        }

        var first = await PageAsync(human, "?limit=5");
        var second = await PageAsync(human, "?limit=5&offset=5");

        Assert.Equal(5, first["entries"]!.AsArray().Count);
        Assert.Equal(first["total"]!.GetValue<int>(), second["total"]!.GetValue<int>());

        // Twelve for the secrets — created and value-set apiece — four for the
        // project and its three environments, and one for the person the first
        // run made: the same log holds what was done to people (ADR 0020), and
        // an unfiltered read is the only place those appear.
        Assert.Equal(17, first["total"]!.GetValue<int>());

        // No entry appears in both pages: one act writes several entries at the
        // same instant, and a boundary in the middle of them has to fall in the
        // same place twice.
        var ids = first["entries"]!.AsArray().Concat(second["entries"]!.AsArray())
            .Select(entry => entry!["id"]!.GetValue<string>())
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The entries carry names and no ids, so a binding cannot narrow the answer
    /// after the fact. Instead the caller names what they are asking about and
    /// the same authority as everywhere else says whether they reach it.
    /// </summary>
    [Fact]
    public async Task A_bound_token_has_to_name_what_it_is_asking_about()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        using var project = await human.GetAsync(
            "/api/v1/projects/webshop-api", TestContext.Current.CancellationToken);

        var shape = JsonNode.Parse(await project.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        using var issued = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new
            {
                kind = "agent",
                name = "the agent on the shop",
                bindings = new[] { new { projectId = shape["id"]!.GetValue<string>() } },
            },
            TestContext.Current.CancellationToken);

        issued.EnsureSuccessStatusCode();

        using var bound = instance.ClientWith(JsonNode.Parse(
            await issued.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken))!["value"]!.GetValue<string>());

        using var whole = await bound.GetAsync(
            "/api/v1/changes", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, whole.StatusCode);
        Assert.Equal("out-of-reach", await IdentityTests.CodeOf(whole));

        using var named = await bound.GetAsync(
            "/api/v1/changes?project=webshop-api", TestContext.Current.CancellationToken);

        named.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Naming an environment without its project would match every project's
    /// <c>prod</c>, which is not what anybody means by it.
    /// </summary>
    [Fact]
    public async Task An_environment_is_named_inside_a_project()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        using var response = await human.GetAsync(
            "/api/v1/changes?environment=prod", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(response));
    }

    /// <summary>
    /// The history says when, and never what. Five old credentials in one answer
    /// would be the bulk disclosure the rest of this product spends every
    /// decision avoiding, and a rollback needs no such thing.
    /// </summary>
    [Fact]
    public async Task The_version_listing_carries_no_value()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the-first-one-nobody-should-see");
        await ReplaceAsync(human, "STRIPE_KEY", "the-second-one-nobody-should-see");

        using var response = await human.GetAsync(
            $"{Secrets}/STRIPE_KEY/versions", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("nobody-should-see", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ciphertext", body, StringComparison.OrdinalIgnoreCase);

        var versions = JsonNode.Parse(body)!.AsArray();

        Assert.Single(versions);
        Assert.NotNull(versions[0]!["expiresAt"]);
    }

    /// <summary>
    /// The undo button (§6.5). An agent that wrecks a value overnight is exactly
    /// who needs one, so it is a write like any other and costs <c>write</c> and
    /// nothing else — no <c>replace</c>, because asking to undo already says what
    /// is meant.
    /// </summary>
    [Fact]
    public async Task An_agent_can_press_undo()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the good one");
        await ReplaceAsync(human, "STRIPE_KEY", "the one the agent wrecked");

        using var agent = instance.ClientWith(await AgentAsync(human, "quiet-otter-42"));

        using var rolled = await agent.PostAsJsonAsync(
            $"{Secrets}/STRIPE_KEY/rollback",
            new { },
            TestContext.Current.CancellationToken);

        rolled.EnsureSuccessStatusCode();

        Assert.Equal("the good one", await GetAsync(human, "STRIPE_KEY"));

        // The value it replaced is now the history, and the one it went back to
        // is not: that would be one credential counted twice against a bound
        // that exists to hold few of them.
        using var versions = await human.GetAsync(
            $"{Secrets}/STRIPE_KEY/versions", TestContext.Current.CancellationToken);

        Assert.Single(JsonNode.Parse(await versions.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray());

        var entries = await ChangesAsync(human, "?project=webshop-api&environment=dev&secret=STRIPE_KEY");

        Assert.Equal("value-rolled-back", entries[0]!["action"]!.GetValue<string>());
        Assert.Equal("agent-token", entries[0]!["identity"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_rollback_can_name_which_version_it_means()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the oldest one");
        await ReplaceAsync(human, "STRIPE_KEY", "the middle one");
        await ReplaceAsync(human, "STRIPE_KEY", "the newest one");

        using var listed = await human.GetAsync(
            $"{Secrets}/STRIPE_KEY/versions", TestContext.Current.CancellationToken);

        var versions = JsonNode.Parse(await listed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray();

        // Newest first, so the second one is the oldest value still kept.
        var oldest = versions[1]!["id"]!.GetValue<string>();

        using var rolled = await human.PostAsJsonAsync(
            $"{Secrets}/STRIPE_KEY/rollback",
            new { versionId = oldest },
            TestContext.Current.CancellationToken);

        rolled.EnsureSuccessStatusCode();

        Assert.Equal("the oldest one", await GetAsync(human, "STRIPE_KEY"));
    }

    [Fact]
    public async Task There_is_nothing_to_roll_back_to_before_the_second_write()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the only one");

        using var response = await human.PostAsJsonAsync(
            $"{Secrets}/STRIPE_KEY/rollback",
            new { },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A token with <c>names</c> and not <c>read</c> may see what happened and
    /// when, and still not open anything — including through the history, which
    /// is where a value would otherwise leak back in.
    /// </summary>
    [Fact]
    public async Task A_value_blind_token_can_read_both_of_these()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-at-all");
        await ReplaceAsync(human, "STRIPE_KEY", "not-a-key-either");

        using var blind = instance.ClientWith(await AgentAsync(human, "the blind one", "names"));

        using var changes = await blind.GetAsync(
            "/api/v1/changes", TestContext.Current.CancellationToken);

        changes.EnsureSuccessStatusCode();

        using var versions = await blind.GetAsync(
            $"{Secrets}/STRIPE_KEY/versions", TestContext.Current.CancellationToken);

        versions.EnsureSuccessStatusCode();

        using var read = await blind.GetAsync(
            $"{Secrets}/STRIPE_KEY", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        // And it cannot press undo either: that is a write.
        using var rolled = await blind.PostAsJsonAsync(
            $"{Secrets}/STRIPE_KEY/rollback",
            new { },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, rolled.StatusCode);
        Assert.Equal("insufficient-scope", await IdentityTests.CodeOf(rolled));
    }

    private static async Task<HttpClient> SetUpAsync(AnInstance instance)
    {
        var human = instance.ClientWith(await instance.StartAsync());

        using var project = await human.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "webshop-api" },
            TestContext.Current.CancellationToken);

        project.EnsureSuccessStatusCode();

        return human;
    }

    private static async Task SetAsync(HttpClient client, string name, string value)
    {
        using var response = await client.PutAsJsonAsync(
            $"{Secrets}/{name}", new { value }, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task ReplaceAsync(HttpClient client, string name, string value)
    {
        using var response = await client.PutAsJsonAsync(
            $"{Secrets}/{name}",
            new { value, replace = true },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<string?> GetAsync(HttpClient client, string name)
    {
        using var response = await client.GetAsync(
            $"{Secrets}/{name}", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]?.GetValue<string>();
    }

    private static async Task<JsonNode> PageAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(
            $"/api/v1/changes{query}", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonArray> ChangesAsync(HttpClient client, string query) =>
        (await PageAsync(client, query))["entries"]!.AsArray();

    private static async Task<string> AgentAsync(
        HttpClient human, string name, params string[] scopes)
    {
        using var response = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            scopes.Length is 0
                ? new { kind = "agent", name, scopes = (string[]?)null }
                : new { kind = "agent", name, scopes = (string[]?)scopes },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]!.GetValue<string>();
    }
}
