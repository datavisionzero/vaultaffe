using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The secrets surface (Specification §6.2, §8): writing without seeing, reading
/// one key on purpose, names without values, a file in and a file out.
/// </summary>
/// <remarks>
/// The line these tests are really about is §4's: every command an agent needs in
/// the normal course of its work manages without a value in its context. So most
/// of what is asserted here is what an answer does <b>not</b> contain.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class SecretEndpointTests(PostgresFixture postgres)
{
    private const string Secrets = "/api/v1/projects/webshop-api/environments/dev/secrets";

    private const string Environment = "/api/v1/projects/webshop-api/environments/dev";

    [Fact]
    public async Task A_value_goes_in_and_the_answer_does_not_bring_it_back()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        using var response = await human.PutAsJsonAsync(
            $"{Secrets}/STRIPE_KEY",
            new { value = "not-a-key-do-not-echo-me" },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("not-a-key-do-not-echo-me", body, StringComparison.Ordinal);
        Assert.Equal("set", JsonNode.Parse(body)!["status"]!.GetValue<string>());
    }

    /// <summary>
    /// The one answer in this product that carries a value, and it was asked for
    /// by name. Multi-line values pass through unchanged — a PEM key is the
    /// normal case, not the exotic one.
    /// </summary>
    [Theory]
    [InlineData("not-a-key-at-all")]
    [InlineData("-----BEGIN KEY-----\nline two\n-----END KEY-----")]
    [InlineData("  spaces and a trailing newline\n")]
    public async Task One_value_comes_back_exactly_as_it_went_in(string value)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", value);

        Assert.Equal(value, await GetAsync(human, "STRIPE_KEY"));
    }

    /// <summary>
    /// §6.2 strips exactly one trailing newline, and does it in the CLI where the
    /// bytes came off a pipe and <c>--raw</c> can say not to. By the time a value
    /// is in a request it is what the caller meant.
    /// </summary>
    [Fact]
    public async Task The_server_trims_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "value\n\n");

        Assert.Equal("value\n\n", await GetAsync(human, "STRIPE_KEY"));
    }

    /// <summary>
    /// Overwriting is as destructive as deleting, and an agent that means to
    /// rotate a credential should say so — the flag is also what makes the
    /// rotation of §8 scenario 4 legible in the change log.
    /// </summary>
    [Fact]
    public async Task Writing_over_a_value_has_to_be_asked_for()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the first one");

        using var refused = await human.PutAsJsonAsync(
            $"{Secrets}/STRIPE_KEY",
            new { value = "the second one" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("replace-required", await IdentityTests.CodeOf(refused));
        Assert.Equal("the first one", await GetAsync(human, "STRIPE_KEY"));

        using var replaced = await human.PutAsJsonAsync(
            $"{Secrets}/STRIPE_KEY",
            new { value = "the second one", replace = true },
            TestContext.Current.CancellationToken);

        replaced.EnsureSuccessStatusCode();

        Assert.Equal("the second one", await GetAsync(human, "STRIPE_KEY"));
    }

    /// <summary>
    /// §8 scenario 3: the agent notices a key is needed, creates it empty, and
    /// tells the human. Filling it afterwards needs no flag — that is what the
    /// placeholder was for.
    /// </summary>
    [Fact]
    public async Task An_empty_placeholder_is_a_state_and_filling_it_needs_no_flag()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        using var created = await human.PutAsJsonAsync(
            $"{Secrets}/SMTP_PASSWORD",
            new { value = (string?)null },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        Assert.Equal(
            "empty",
            JsonNode.Parse(await created.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken))!["status"]!.GetValue<string>());

        // Asked for by name, a placeholder answers with no value rather than with
        // an empty one: there is nothing there, and silence is what it is for.
        using var read = await human.GetAsync(
            $"{Secrets}/SMTP_PASSWORD", TestContext.Current.CancellationToken);

        read.EnsureSuccessStatusCode();

        var answer = JsonNode.Parse(await read.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Null(answer["value"]);
        Assert.Equal("empty", answer["secret"]!["status"]!.GetValue<string>());

        using var filled = await human.PutAsJsonAsync(
            $"{Secrets}/SMTP_PASSWORD",
            new { value = "what the human knew" },
            TestContext.Current.CancellationToken);

        filled.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A placeholder is not a way of emptying a key. Asking for one where a value
    /// already is would be asking to throw it away by saying nothing.
    /// </summary>
    [Fact]
    public async Task A_placeholder_cannot_be_put_over_a_value()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "a value");

        using var refused = await human.PutAsJsonAsync(
            $"{Secrets}/STRIPE_KEY",
            new { value = (string?)null },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("a value", await GetAsync(human, "STRIPE_KEY"));
    }

    /// <summary>
    /// An empty string is not a value. It is what a placeholder means, and one
    /// standing in for the other is the bug the placeholder exists to prevent.
    /// </summary>
    [Fact]
    public async Task An_empty_string_is_refused_rather_than_stored()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        using var refused = await human.PutAsJsonAsync(
            $"{Secrets}/STRIPE_KEY",
            new { value = "" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(refused));
    }

    /// <summary>
    /// The normal case for an agent, and the shape a later MCP server needs:
    /// which keys exist, which of them a human still has to fill, and nothing
    /// else at all.
    /// </summary>
    [Fact]
    public async Task The_listing_carries_names_and_status_and_no_value_anywhere()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-do-not-list-me");
        await SetAsync(human, "DATABASE_URL", "postgres://do_not_list_me");

        using var placeholder = await human.PutAsJsonAsync(
            $"{Secrets}/SMTP_PASSWORD",
            new { value = (string?)null },
            TestContext.Current.CancellationToken);

        placeholder.EnsureSuccessStatusCode();

        using var listed = await human.GetAsync(Secrets, TestContext.Current.CancellationToken);

        var body = await listed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("do_not_list_me", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\"", body, StringComparison.Ordinal);

        var secrets = JsonNode.Parse(body)!.AsArray();

        Assert.Equal(
            [("DATABASE_URL", "set"), ("SMTP_PASSWORD", "empty"), ("STRIPE_KEY", "set")],
            secrets.Select(secret => (
                secret!["name"]!.GetValue<string>(), secret["status"]!.GetValue<string>())));
    }

    /// <summary>
    /// <c>names</c> is a scope of its own rather than half of "read", and this is
    /// what that buys: a token that can see which keys exist and which are still
    /// empty, and cannot read one.
    /// </summary>
    [Fact]
    public async Task A_token_with_names_and_not_read_sees_the_keys_and_no_value()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-at-all");

        using var blind = instance.ClientWith(await TokenAsync(human, "agent", "names"));

        using var listed = await blind.GetAsync(Secrets, TestContext.Current.CancellationToken);

        listed.EnsureSuccessStatusCode();

        using var read = await blind.GetAsync(
            $"{Secrets}/STRIPE_KEY", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal("insufficient-scope", await IdentityTests.CodeOf(read));
    }

    /// <summary>
    /// "May write but never read" is the case a read/read-write switch cannot
    /// express, and in a secrets manager reading is the dangerous half. An agent
    /// piping a vendor's output straight in needs exactly this token.
    /// </summary>
    [Fact]
    public async Task A_token_that_may_write_and_not_read_can_still_rotate_a_credential()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the old one");

        using var writer = instance.ClientWith(await TokenAsync(human, "agent", "names", "write"));

        using var written = await writer.PutAsJsonAsync(
            $"{Secrets}/STRIPE_KEY",
            new { value = "the new one", replace = true },
            TestContext.Current.CancellationToken);

        written.EnsureSuccessStatusCode();

        using var read = await writer.GetAsync(
            $"{Secrets}/STRIPE_KEY", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        // And the human, who may read, sees what the agent wrote without the
        // agent ever having seen either value.
        Assert.Equal("the new one", await GetAsync(human, "STRIPE_KEY"));
    }

    [Fact]
    public async Task Deleting_is_recoverable_here_too()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-at-all");

        using var deleted = await human.DeleteAsync(
            $"{Secrets}/STRIPE_KEY", TestContext.Current.CancellationToken);

        deleted.EnsureSuccessStatusCode();

        using var gone = await human.GetAsync(
            $"{Secrets}/STRIPE_KEY", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        // The name is held for as long as it can be restored, so writing over it
        // is refused rather than silently replacing something recoverable.
        using var again = await human.PutAsJsonAsync(
            $"{Secrets}/STRIPE_KEY",
            new { value = "a different one" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("name-taken", await IdentityTests.CodeOf(again));

        using var restored = await human.PostAsync(
            $"{Secrets}/STRIPE_KEY/restore", null, TestContext.Current.CancellationToken);

        restored.EnsureSuccessStatusCode();

        Assert.Equal("not-a-key-at-all", await GetAsync(human, "STRIPE_KEY"));
    }

    /// <summary>
    /// Migration is a success criterion (§11) and must not be UI-only: the whole
    /// file goes in at once, and what comes back names keys and never values —
    /// so an agent can migrate a file it never displays.
    /// </summary>
    [Fact]
    public async Task A_file_goes_in_and_the_summary_names_keys_and_no_values()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the one already here");

        using var response = await human.PostAsJsonAsync(
            $"{Environment}/import",
            new
            {
                content =
                    """
                    # from the old machine
                    DATABASE_URL=postgres://localhost/app
                    export STRIPE_KEY=not-a-key-from-the-file
                    SMTP_PASSWORD=
                    not a setting
                    """,
            },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("postgres://localhost/app", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-key-from-the-file", body, StringComparison.Ordinal);

        var summary = JsonNode.Parse(body)!;

        Assert.Equal(
            ["DATABASE_URL", "SMTP_PASSWORD"],
            summary["created"]!.AsArray().Select(name => name!.GetValue<string>()));

        // The key that already held a value was left alone: overwriting is
        // explicit here exactly as it is one key at a time.
        Assert.Equal(
            ["STRIPE_KEY"],
            summary["skipped"]!.AsArray().Select(one => one!["name"]!.GetValue<string>()));

        Assert.Equal([5], summary["unreadable"]!.AsArray()
            .Select(one => one!["line"]!.GetValue<int>()));

        Assert.Equal("the one already here", await GetAsync(human, "STRIPE_KEY"));

        // KEY= is what a file says when a key is there and its value is not.
        var listing = await ListAsync(human);

        Assert.Equal(
            "empty",
            listing.Single(one => one!["name"]!.GetValue<string>() == "SMTP_PASSWORD")!["status"]!
                .GetValue<string>());
    }

    [Fact]
    public async Task An_import_that_may_replace_says_which_keys_it_replaced()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the old one");

        using var response = await human.PostAsJsonAsync(
            $"{Environment}/import",
            new { content = "STRIPE_KEY=not-a-key-from-the-file\n", replace = true },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var summary = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal(
            ["STRIPE_KEY"],
            summary["replaced"]!.AsArray().Select(name => name!.GetValue<string>()));

        Assert.Equal("not-a-key-from-the-file", await GetAsync(human, "STRIPE_KEY"));
    }

    /// <summary>
    /// The export is the contradiction <c>inject</c> was rejected for (§7). It
    /// stays because a way back out is part of being trustworthy, and it is
    /// behind a session because the output is every value of an environment at
    /// once — which is the same reason the rest of the human-only list exists.
    /// </summary>
    [Fact]
    public async Task Export_writes_everything_and_only_for_a_person()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-at-all");
        await SetAsync(human, "MULTILINE", "-----BEGIN-----\nbody\n-----END-----");

        using var exported = await human.GetAsync(
            $"{Environment}/export", TestContext.Current.CancellationToken);

        exported.EnsureSuccessStatusCode();

        var file = await exported.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("STRIPE_KEY=\"not-a-key-at-all\"", file, StringComparison.Ordinal);
        Assert.Contains(
            "MULTILINE=\"-----BEGIN-----\\nbody\\n-----END-----\"", file, StringComparison.Ordinal);

        // An agent with every scope there is still does not get one: being
        // human-only is not a permission a token can be given.
        using var agent = instance.ClientWith(
            await TokenAsync(human, "agent", "names", "read", "write", "delete"));

        using var refused = await agent.GetAsync(
            $"{Environment}/export", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var problem = JsonNode.Parse(await refused.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("human-only", problem["code"]!.GetValue<string>());
        Assert.Equal("export", problem["humanAction"]!.GetValue<string>());
        Assert.DoesNotContain("not-a-key-at-all", problem.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// What is exported can be imported: the format quotes and escapes every
    /// value, so a round trip through a file is a round trip and not an
    /// approximation.
    /// </summary>
    [Fact]
    public async Task What_is_exported_can_be_imported_into_another_environment()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "MULTILINE", "-----BEGIN-----\nbody\\with\"quotes\n-----END-----");
        await SetAsync(human, "SPACED", "  two words  ");

        using var exported = await human.GetAsync(
            $"{Environment}/export", TestContext.Current.CancellationToken);

        var file = await exported.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var imported = await human.PostAsJsonAsync(
            "/api/v1/projects/webshop-api/environments/prod/import",
            new { content = file },
            TestContext.Current.CancellationToken);

        imported.EnsureSuccessStatusCode();

        Assert.Equal(
            "-----BEGIN-----\nbody\\with\"quotes\n-----END-----",
            await GetAsync(human, "MULTILINE", "prod"));

        Assert.Equal("  two words  ", await GetAsync(human, "SPACED", "prod"));
    }

    /// <summary>
    /// Five versions, 72 hours, whichever is reached first (§6.5). Every retained
    /// old value is usually a still-valid credential, which is the whole reason
    /// there is a bound — and what falls out is deleted rather than tombstoned.
    /// </summary>
    [Fact]
    public async Task The_value_history_stays_inside_its_bounds()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "version 0");

        for (var version = 1; version <= 8; version++)
        {
            using var written = await human.PutAsJsonAsync(
                $"{Secrets}/STRIPE_KEY",
                new { value = $"version {version}", replace = true },
                TestContext.Current.CancellationToken);

            written.EnsureSuccessStatusCode();
        }

        Assert.Equal(5, await VersionsAsync(instance, human));

        // And the window, which the count never reaches: three writes, then time
        // enough for all of them to fall out.
        instance.Clock.MoveOn(TimeSpan.FromHours(73));

        using var last = await human.PutAsJsonAsync(
            $"{Secrets}/STRIPE_KEY",
            new { value = "the one after the window", replace = true },
            TestContext.Current.CancellationToken);

        last.EnsureSuccessStatusCode();

        Assert.Equal(1, await VersionsAsync(instance, human));
    }

    [Fact]
    public async Task A_key_that_could_not_be_an_environment_variable_is_refused()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        using var response = await human.PutAsJsonAsync(
            $"{Secrets}/lower_case",
            new { value = "a value" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(response));
    }

    /// <summary>
    /// A token bound to one environment cannot read the one beside it, which is
    /// what "exclude production" is supposed to mean when a human sets it.
    /// </summary>
    [Fact]
    public async Task A_binding_keeps_a_token_out_of_the_environment_beside_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the development one");

        using var project = await human.GetAsync(
            "/api/v1/projects/webshop-api", TestContext.Current.CancellationToken);

        var environments = JsonNode.Parse(await project.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        var dev = environments["environments"]!.AsArray()
            .Single(one => one!["name"]!.GetValue<string>() == "dev")!;

        using var issued = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new
            {
                kind = "agent",
                name = "the agent kept out of production",
                bindings = new[]
                {
                    new
                    {
                        projectId = environments["id"]!.GetValue<string>(),
                        environmentId = dev["id"]!.GetValue<string>(),
                    },
                },
            },
            TestContext.Current.CancellationToken);

        issued.EnsureSuccessStatusCode();

        using var bound = instance.ClientWith(JsonNode.Parse(
            await issued.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken))!["value"]!.GetValue<string>());

        using var allowed = await bound.GetAsync(
            $"{Secrets}/STRIPE_KEY", TestContext.Current.CancellationToken);

        allowed.EnsureSuccessStatusCode();

        using var refused = await bound.GetAsync(
            "/api/v1/projects/webshop-api/environments/prod/secrets",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("out-of-reach", await IdentityTests.CodeOf(refused));
    }

    /// <summary>An instance with one project, and the session that made it.</summary>
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

    private static async Task<string?> GetAsync(
        HttpClient client, string name, string environment = "dev")
    {
        using var response = await client.GetAsync(
            $"/api/v1/projects/webshop-api/environments/{environment}/secrets/{name}",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]?.GetValue<string>();
    }

    private static async Task<JsonArray> ListAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            Secrets, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray();
    }

    private static async Task<string> TokenAsync(
        HttpClient human, string kind, params string[] scopes)
    {
        using var response = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind, name = $"a {kind} token", scopes },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]!.GetValue<string>();
    }

    /// <summary>How many superseded values this instance is holding.</summary>
    private static async Task<int> VersionsAsync(AnInstance instance, HttpClient client)
    {
        using var me = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        me.EnsureSuccessStatusCode();

        var organizationId = JsonNode.Parse(await me.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["organizationId"]!.GetValue<Guid>();

        await using var context = Migrated.ReaderOn(instance.ConnectionString, organizationId);

        return await context.SecretValueVersions.CountAsync(TestContext.Current.CancellationToken);
    }
}
