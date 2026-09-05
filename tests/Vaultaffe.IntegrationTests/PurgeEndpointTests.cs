using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vaultaffe.Infrastructure.Persistence;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// Recoverable deletion and purge (Specification §6.5): the window, what a
/// deleted container keeps, what a person may remove early, and what a deadline
/// removes on its own.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PurgeEndpointTests(PostgresFixture postgres)
{
    private const string Project = "/api/v1/projects/webshop-api";

    private const string Secrets = "/api/v1/projects/webshop-api/environments/dev/secrets";

    /// <summary>
    /// Purge is a visible feature and not a support ticket. A feature nothing can
    /// list is one, so the deleted objects are their own listing at every level.
    /// </summary>
    [Fact]
    public async Task What_is_deleted_can_be_listed_at_every_level()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-at-all");

        await DeleteAsync(human, $"{Secrets}/STRIPE_KEY");
        await DeleteAsync(human, $"{Project}/environments/staging");

        Assert.Equal(["STRIPE_KEY"], await NamesAsync(human, $"{Secrets}?deleted=true"));
        Assert.Empty(await NamesAsync(human, Secrets));

        Assert.Equal(
            ["staging"], await NamesAsync(human, $"{Project}/environments?deleted=true"));

        await DeleteAsync(human, Project);

        Assert.Equal(
            ["webshop-api"], await NamesAsync(human, "/api/v1/projects?deleted=true"));

        Assert.Empty(await NamesAsync(human, "/api/v1/projects"));
    }

    /// <summary>
    /// Purge is human-only: it is the one action that destroys the undo button,
    /// and in an agent's hands it would be anti-forensics. Scopes do not buy it —
    /// an agent with every one of them is still refused.
    /// </summary>
    [Fact]
    public async Task No_token_purges_anything_however_many_scopes_it_carries()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-at-all");
        await DeleteAsync(human, $"{Secrets}/STRIPE_KEY");

        using var agent = instance.ClientWith(await AgentAsync(human));

        foreach (var path in new[]
        {
            $"{Secrets}/STRIPE_KEY/purge",
            $"{Project}/environments/staging/purge",
            $"{Project}/purge",
        })
        {
            using var refused = await agent.PostAsync(
                path, null, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

            var problem = JsonNode.Parse(await refused.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken))!;

            Assert.Equal("human-only", problem["code"]!.GetValue<string>());
            Assert.Equal("purge", problem["humanAction"]!.GetValue<string>());
        }

        using var history = await agent.DeleteAsync(
            $"{Secrets}/DATABASE_URL/versions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, history.StatusCode);
        Assert.Equal("human-only", await IdentityTests.CodeOf(history));
    }

    /// <summary>
    /// A purge is the second half of a deletion, not a faster one. Asking for it
    /// on something in use would be asking to skip the window that makes deleting
    /// safe in the first place.
    /// </summary>
    [Fact]
    public async Task Only_something_already_deleted_can_be_purged()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-at-all");

        using var secret = await human.PostAsync(
            $"{Secrets}/STRIPE_KEY/purge", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, secret.StatusCode);

        using var project = await human.PostAsync(
            $"{Project}/purge", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, project.StatusCode);
        Assert.Equal("not-a-key-at-all", await GetAsync(human, "STRIPE_KEY"));
    }

    /// <summary>
    /// The headline case: after a suspected compromise the normal expectation is
    /// that a key's history genuinely disappears. The secret and its current value
    /// stay — this removes what it used to hold, and nothing else.
    /// </summary>
    [Fact]
    public async Task A_key_s_history_can_be_made_to_genuinely_disappear()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the compromised one");
        await ReplaceAsync(human, "STRIPE_KEY", "the one after it");
        await ReplaceAsync(human, "STRIPE_KEY", "the current one");

        Assert.Equal(2, await VersionsAsync(instance, human));

        using var purged = await human.DeleteAsync(
            $"{Secrets}/STRIPE_KEY/versions", TestContext.Current.CancellationToken);

        purged.EnsureSuccessStatusCode();

        var summary = JsonNode.Parse(await purged.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal(2, summary["versions"]!.GetValue<int>());
        Assert.Equal(0, await VersionsAsync(instance, human));

        // The secret is untouched, and there is nothing left to roll back to.
        Assert.Equal("the current one", await GetAsync(human, "STRIPE_KEY"));

        using var rolled = await human.PostAsJsonAsync(
            $"{Secrets}/STRIPE_KEY/rollback",
            new { },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, rolled.StatusCode);
    }

    /// <summary>
    /// Purging a container takes the retained subtree with it, because there is
    /// no cascade between the levels in the schema and this is the one place
    /// where permanent is what was asked for.
    /// </summary>
    [Fact]
    public async Task Purging_a_project_takes_the_subtree_that_was_retained_with_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the first one");
        await ReplaceAsync(human, "STRIPE_KEY", "the second one");

        await DeleteAsync(human, Project);

        using var purged = await human.PostAsync(
            $"{Project}/purge", null, TestContext.Current.CancellationToken);

        purged.EnsureSuccessStatusCode();

        Assert.Empty(await NamesAsync(human, "/api/v1/projects?deleted=true"));
        Assert.Equal(0, await VersionsAsync(instance, human));
        Assert.Equal(0, await CountAsync<Domain.Secrets.Secret>(instance, human));

        // The name is free again, which is what the reservation was waiting for.
        using var again = await human.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "webshop-api" },
            TestContext.Current.CancellationToken);

        again.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Neither purge nor expiry removes change-log entries. That is what keeps
    /// the log able to say what happened to something that no longer exists —
    /// and it is why it records names rather than keys.
    /// </summary>
    [Fact]
    public async Task The_log_outlives_everything_a_purge_removes()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-at-all");
        await DeleteAsync(human, Project);

        using var purged = await human.PostAsync(
            $"{Project}/purge", null, TestContext.Current.CancellationToken);

        purged.EnsureSuccessStatusCode();

        using var changes = await human.GetAsync(
            "/api/v1/changes", TestContext.Current.CancellationToken);

        changes.EnsureSuccessStatusCode();

        var entries = JsonNode.Parse(await changes.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["entries"]!.AsArray();

        Assert.Equal("purged", entries[0]!["action"]!.GetValue<string>());
        Assert.Equal("webshop-api", entries[0]!["project"]!.GetValue<string>());

        Assert.Contains(
            entries, entry => entry!["secret"]?.GetValue<string>() == "STRIPE_KEY");
    }

    /// <summary>
    /// A window nothing enforces is a promise rather than a window. The sweep is
    /// part of the installation for that reason, and this is it doing its job:
    /// past 72 hours the row is gone, the name is free, and the log is not.
    /// </summary>
    [Fact]
    public async Task The_deadline_removes_what_nobody_purged()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "not-a-key-at-all");
        await DeleteAsync(human, Project);

        instance.Clock.MoveOn(TimeSpan.FromHours(73));

        await SweptAsync(instance);

        Assert.Equal(0, await CountAsync<Domain.Projects.Project>(instance, human));
        Assert.Equal(0, await CountAsync<Domain.Secrets.Secret>(instance, human));

        using var again = await human.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "webshop-api" },
            TestContext.Current.CancellationToken);

        again.EnsureSuccessStatusCode();

        // Nothing removed an entry, which is the property that makes the log
        // worth keeping at all.
        using var changes = await human.GetAsync(
            "/api/v1/changes", TestContext.Current.CancellationToken);

        Assert.Contains(
            "STRIPE_KEY",
            await changes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A superseded value goes when its own window ends, whether or not anybody
    /// writes to that secret again — otherwise a still-valid credential sits in
    /// the database until the next rotation, which is the opposite of a bound.
    /// </summary>
    [Fact]
    public async Task The_deadline_removes_a_superseded_value_nobody_wrote_over()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "STRIPE_KEY", "the old one");
        await ReplaceAsync(human, "STRIPE_KEY", "the current one");

        Assert.Equal(1, await VersionsAsync(instance, human));

        instance.Clock.MoveOn(TimeSpan.FromHours(73));

        await SweptAsync(instance);

        Assert.Equal(0, await VersionsAsync(instance, human));
        Assert.Equal("the current one", await GetAsync(human, "STRIPE_KEY"));
    }

    /// <summary>
    /// Deleting a container does not restart the clocks underneath it: a
    /// descendant deleted first keeps its original deadline, and expires on it.
    /// </summary>
    [Fact]
    public async Task A_descendant_deleted_first_keeps_its_own_deadline()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await DeleteAsync(human, $"{Project}/environments/staging");

        instance.Clock.MoveOn(TimeSpan.FromHours(40));

        await DeleteAsync(human, Project);

        // Forty more hours: the environment is over its 72, the project is not.
        instance.Clock.MoveOn(TimeSpan.FromHours(40));

        await SweptAsync(instance);

        Assert.Equal(1, await CountAsync<Domain.Projects.Project>(instance, human));
        Assert.Equal(
            2, await CountAsync<Domain.Environments.Environment>(instance, human));

        using var restored = await human.PostAsync(
            $"{Project}/restore", null, TestContext.Current.CancellationToken);

        restored.EnsureSuccessStatusCode();

        Assert.Equal(
            ["dev", "prod"], await NamesAsync(human, $"{Project}/environments"));
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

    private static async Task DeleteAsync(HttpClient client, string path)
    {
        using var response = await client.DeleteAsync(path, TestContext.Current.CancellationToken);

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

    private static async Task<IReadOnlyList<string>> NamesAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return
        [
            .. JsonNode.Parse(await response.Content.ReadAsStringAsync(
                    TestContext.Current.CancellationToken))!
                .AsArray()
                .Select(one => one!["name"]!.GetValue<string>()),
        ];
    }

    private static async Task<string> AgentAsync(HttpClient human)
    {
        using var response = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new
            {
                kind = "agent",
                name = "the agent with every scope",
                scopes = new[] { "names", "read", "write", "delete" },
            },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]!.GetValue<string>();
    }

    /// <summary>
    /// One pass of the sweep this instance runs on a timer — the same object, run
    /// on demand, because a test that waited fifteen minutes for a tick would be
    /// testing the timer rather than the deadline.
    /// </summary>
    private static Task SweptAsync(AnInstance instance) =>
        instance.Services.GetRequiredService<ExpirySweep>()
            .RunAsync(instance.Clock.GetUtcNow(), TestContext.Current.CancellationToken);

    private static async Task<int> VersionsAsync(AnInstance instance, HttpClient client) =>
        await CountAsync<Domain.Secrets.SecretValueVersion>(instance, client);

    /// <summary>How many rows of that table this instance is holding.</summary>
    private static async Task<int> CountAsync<T>(AnInstance instance, HttpClient client)
        where T : class
    {
        using var me = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        me.EnsureSuccessStatusCode();

        var organizationId = JsonNode.Parse(await me.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["organizationId"]!.GetValue<Guid>();

        await using var context = Migrated.ReaderOn(instance.ConnectionString, organizationId);

        return await context.Set<T>().CountAsync(TestContext.Current.CancellationToken);
    }
}
