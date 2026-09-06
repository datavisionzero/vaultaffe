using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The access summary (Specification §6.5): first and last use per identity and
/// secret.
/// </summary>
/// <remarks>
/// What these tests are really about is the line the summary must not cross. A
/// successful read does not prove that an application started, so what is
/// asserted here is two moments and an identity — and, as often, what is
/// <b>not</b> there: no count, no entry per read, and no value anywhere near it.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class AccessSummaryTests(PostgresFixture postgres)
{
    private const string Environment = "/api/v1/projects/webshop-api/environments/dev";

    private const string Secrets = $"{Environment}/secrets";

    [Fact]
    public async Task Reading_a_value_is_a_first_and_a_last_use()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await ReadAsync(human, "STRIPE_KEY");

        var line = (await SummaryAsync(human, "STRIPE_KEY")).Single()!;

        Assert.Equal("human-session", line["identity"]!["type"]!.GetValue<string>());
        Assert.Equal("Maintainer", line["identity"]!["name"]!.GetValue<string>());
        Assert.Equal(
            line["firstAt"]!.GetValue<DateTimeOffset>(),
            line["lastAt"]!.GetValue<DateTimeOffset>());
    }

    /// <summary>
    /// The whole shape of it: the last moment moves, the first one never does,
    /// and reading a hundred times is still one line.
    /// </summary>
    [Fact]
    public async Task Reading_again_moves_the_last_moment_and_not_the_first()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await ReadAsync(human, "STRIPE_KEY");

        var first = (await SummaryAsync(human, "STRIPE_KEY")).Single()!;
        var firstAt = first["firstAt"]!.GetValue<DateTimeOffset>();

        instance.Clock.MoveOn(TimeSpan.FromHours(2));

        await ReadAsync(human, "STRIPE_KEY");
        await ReadAsync(human, "STRIPE_KEY");

        var again = await SummaryAsync(human, "STRIPE_KEY");
        var line = Assert.Single(again)!;

        Assert.Equal(firstAt, line["firstAt"]!.GetValue<DateTimeOffset>());
        Assert.True(line["lastAt"]!.GetValue<DateTimeOffset>() > firstAt);
    }

    /// <summary>
    /// The field §6.5 exists for. An agent reading under its own token is its own
    /// line, and is not the person whose terminal it sits in.
    /// </summary>
    [Fact]
    public async Task An_agent_is_a_line_of_its_own_and_says_it_is_an_agent()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await ReadAsync(human, "STRIPE_KEY");

        using var agent = instance.ClientWith(await TokenAsync(human, "agent"));

        await ReadAsync(agent, "STRIPE_KEY");

        var lines = await SummaryAsync(human, "STRIPE_KEY");

        Assert.Equal(
            ["agent-token", "human-session"],
            lines.Select(one => one!["identity"]!["type"]!.GetValue<string>()).Order());
    }

    /// <summary>
    /// A listing is not a use of anything. It answers names and status and reads
    /// no value, so nothing about it belongs in a summary of who read what.
    /// </summary>
    [Fact]
    public async Task A_listing_is_not_an_access()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        using var listed = await human.GetAsync(Secrets, TestContext.Current.CancellationToken);

        listed.EnsureSuccessStatusCode();

        Assert.Empty(await SummaryAsync(human, "STRIPE_KEY"));
    }

    /// <summary>
    /// An export reads every value of the environment at once. Leaving it out
    /// would make the one read that takes everything the one the summary does not
    /// know about.
    /// </summary>
    [Fact]
    public async Task An_export_is_an_access_to_every_key_in_the_environment()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "DATABASE_URL", "postgres://somewhere");

        using var exported = await human.GetAsync(
            $"{Environment}/export", TestContext.Current.CancellationToken);

        exported.EnsureSuccessStatusCode();

        Assert.Single(await SummaryAsync(human, "STRIPE_KEY"));
        Assert.Single(await SummaryAsync(human, "DATABASE_URL"));
    }

    /// <summary>
    /// A summary of who read a key, and nothing about what they read — the same
    /// rule the change log is held to.
    /// </summary>
    [Fact]
    public async Task The_summary_carries_no_value_and_no_count()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await ReadAsync(human, "STRIPE_KEY");

        using var response = await human.GetAsync(
            $"{Secrets}/STRIPE_KEY/access", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("not-a-key-do-not-echo-me", body, StringComparison.Ordinal);
        Assert.DoesNotContain("count", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// When a secret is genuinely gone, what was recorded about reading it has to
    /// be gone with it. That is what a purge means (§6.5).
    /// </summary>
    [Fact]
    public async Task A_purge_takes_the_summary_with_the_secret()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await ReadAsync(human, "STRIPE_KEY");

        using var deleted = await human.DeleteAsync(
            $"{Secrets}/STRIPE_KEY", TestContext.Current.CancellationToken);

        deleted.EnsureSuccessStatusCode();

        using var purged = await human.PostAsync(
            $"{Secrets}/STRIPE_KEY/purge", content: null, TestContext.Current.CancellationToken);

        purged.EnsureSuccessStatusCode();

        await SetAsync(human, "STRIPE_KEY", "a different credential entirely");

        Assert.Empty(await SummaryAsync(human, "STRIPE_KEY"));
    }

    /// <summary>An instance with one project and one key that holds a value.</summary>
    private static async Task<HttpClient> SetUpAsync(AnInstance instance)
    {
        var human = instance.ClientWith(await instance.StartAsync());

        using var project = await human.PostAsJsonAsync(
            "/api/v1/projects",
            new { name = "webshop-api" },
            TestContext.Current.CancellationToken);

        project.EnsureSuccessStatusCode();

        await SetAsync(human, "STRIPE_KEY", "not-a-key-do-not-echo-me");

        return human;
    }

    private static async Task SetAsync(HttpClient client, string name, string value)
    {
        using var response = await client.PutAsJsonAsync(
            $"{Secrets}/{name}", new { value }, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task ReadAsync(HttpClient client, string name)
    {
        using var response = await client.GetAsync(
            $"{Secrets}/{name}", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonArray> SummaryAsync(HttpClient client, string name)
    {
        using var response = await client.GetAsync(
            $"{Secrets}/{name}/access", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray();
    }

    private static async Task<string> TokenAsync(HttpClient human, string kind)
    {
        using var response = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind, name = $"a {kind} token" },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]!.GetValue<string>();
    }
}
