using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The missing-key notice (Specification §6.1): a display with a way to say
/// "not here", and nothing that writes.
/// </summary>
/// <remarks>
/// The rule itself is arithmetic and is held by <c>MissingKeyTests</c> without a
/// database. What is asked here is the rest of it: that the notice is computed
/// over the environments the caller may actually see, that a dismissal survives
/// and can be taken back, and that reading it needs the scope that is about names.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class MissingKeyEndpointTests(PostgresFixture postgres)
{
    private const string Project = "/api/v1/projects/webshop-api";

    [Fact]
    public async Task What_the_others_agree_on_and_this_one_has_not_is_the_notice()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "dev", "STRIPE_KEY");
        await SetAsync(human, "staging", "STRIPE_KEY");

        var notice = await NoticeAsync(human, "prod");

        Assert.Equal("STRIPE_KEY", notice.Single()!["name"]!.GetValue<string>());
        Assert.Equal(
            ["dev", "staging"],
            notice.Single()!["presentIn"]!.AsArray().Select(one => one!.GetValue<string>()));
        Assert.Null(notice.Single()!["dismissedAt"]);
    }

    /// <summary>Production's own keys are nobody's alarm.</summary>
    [Fact]
    public async Task A_key_one_environment_alone_has_is_not_reported()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "prod", "SENTRY_DSN");

        Assert.Empty(await NoticeAsync(human, "dev"));
    }

    /// <summary>
    /// Say "not here" once and it stays said — that is the whole affordance §6.1
    /// asks for, and the answer to a personal environment that will never hold
    /// the key.
    /// </summary>
    [Fact]
    public async Task A_dismissal_stands_and_can_be_taken_back()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "dev", "STRIPE_KEY");
        await SetAsync(human, "staging", "STRIPE_KEY");

        using var dismissed = await human.PostAsync(
            $"{Project}/environments/prod/missing/STRIPE_KEY/dismissal",
            content: null,
            TestContext.Current.CancellationToken);

        dismissed.EnsureSuccessStatusCode();

        Assert.Empty(await SaidAsync(human, "prod"));

        var silenced = await SilencedAsync(human, "prod");

        Assert.Equal("STRIPE_KEY", silenced.Single()!["name"]!.GetValue<string>());
        Assert.NotNull(silenced.Single()!["dismissedAt"]);

        using var withdrawn = await human.DeleteAsync(
            $"{Project}/environments/prod/missing/STRIPE_KEY/dismissal",
            TestContext.Current.CancellationToken);

        withdrawn.EnsureSuccessStatusCode();

        Assert.Equal(
            "STRIPE_KEY", (await SaidAsync(human, "prod")).Single()!["name"]!.GetValue<string>());
    }

    /// <summary>
    /// Two people looking at the same notice is the normal case, and the second
    /// of them should not be told the key does not exist.
    /// </summary>
    [Fact]
    public async Task Dismissing_twice_is_dismissing_once()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "dev", "STRIPE_KEY");
        await SetAsync(human, "staging", "STRIPE_KEY");

        using var first = await human.PostAsync(
            $"{Project}/environments/prod/missing/STRIPE_KEY/dismissal",
            content: null,
            TestContext.Current.CancellationToken);

        first.EnsureSuccessStatusCode();

        using var again = await human.PostAsync(
            $"{Project}/environments/prod/missing/STRIPE_KEY/dismissal",
            content: null,
            TestContext.Current.CancellationToken);

        again.EnsureSuccessStatusCode();

        Assert.Single(await SilencedAsync(human, "prod"));
    }

    /// <summary>Nothing is dismissed about a key the notice never mentioned.</summary>
    [Fact]
    public async Task There_is_nothing_to_dismiss_where_there_is_no_notice()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "prod", "SENTRY_DSN");

        using var response = await human.PostAsync(
            $"{Project}/environments/dev/missing/SENTRY_DSN/dismissal",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The binding is what a token may touch (§6.4), and a comparison is a way
    /// of touching: a token bound to one environment learns nothing here about
    /// the keys of another. It gets an empty notice rather than a filtered one,
    /// because there is nothing left to compare with.
    /// </summary>
    [Fact]
    public async Task A_token_bound_to_one_environment_is_told_nothing_about_the_others()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "dev", "STRIPE_KEY");
        await SetAsync(human, "staging", "STRIPE_KEY");

        using var bound = instance.ClientWith(await BoundTokenAsync(human, "prod"));

        using var response = await bound.GetAsync(
            $"{Project}/environments/prod/missing", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var notice = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray();

        Assert.Empty(notice);
    }

    /// <summary>
    /// Reading is <c>names</c>, because that is all it ever says. Dismissing is
    /// <c>write</c>, because it changes what everybody sees.
    /// </summary>
    [Fact]
    public async Task Reading_is_names_and_dismissing_is_write()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "dev", "STRIPE_KEY");
        await SetAsync(human, "staging", "STRIPE_KEY");

        using var blind = instance.ClientWith(await TokenAsync(human, "names"));

        using var read = await blind.GetAsync(
            $"{Project}/environments/prod/missing", TestContext.Current.CancellationToken);

        read.EnsureSuccessStatusCode();

        using var refused = await blind.PostAsync(
            $"{Project}/environments/prod/missing/STRIPE_KEY/dismissal",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("insufficient-scope", await IdentityTests.CodeOf(refused));
    }

    /// <summary>
    /// A dismissal is about a place, so it goes with the place. The foreign key
    /// is <c>restrict</c> like everything else between the containers, so a purge
    /// that forgot the dismissals would fail at the database rather than quietly.
    /// </summary>
    [Fact]
    public async Task Purging_an_environment_takes_its_dismissals_with_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = await SetUpAsync(instance);

        await SetAsync(human, "dev", "STRIPE_KEY");
        await SetAsync(human, "staging", "STRIPE_KEY");

        using var dismissed = await human.PostAsync(
            $"{Project}/environments/prod/missing/STRIPE_KEY/dismissal",
            content: null,
            TestContext.Current.CancellationToken);

        dismissed.EnsureSuccessStatusCode();

        using var deleted = await human.DeleteAsync(
            $"{Project}/environments/prod", TestContext.Current.CancellationToken);

        deleted.EnsureSuccessStatusCode();

        using var purged = await human.PostAsync(
            $"{Project}/environments/prod/purge",
            content: null,
            TestContext.Current.CancellationToken);

        purged.EnsureSuccessStatusCode();
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

    /// <summary>
    /// A key in an environment. A placeholder rather than a value, because the
    /// notice is about which keys exist and never about what is in them.
    /// </summary>
    private static async Task SetAsync(HttpClient client, string environment, string name)
    {
        using var response = await client.PutAsJsonAsync(
            $"{Project}/environments/{environment}/secrets/{name}",
            new { value = (string?)null },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>The whole notice: what is still said, and what somebody silenced.</summary>
    private static async Task<JsonArray> NoticeAsync(HttpClient client, string environment)
    {
        using var response = await client.GetAsync(
            $"{Project}/environments/{environment}/missing",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!.AsArray();
    }

    /// <summary>The lines of it nobody has silenced.</summary>
    private static async Task<IReadOnlyList<JsonNode?>> SaidAsync(
        HttpClient client, string environment) =>
        [.. (await NoticeAsync(client, environment)).Where(one => one!["dismissedAt"] is null)];

    private static async Task<IReadOnlyList<JsonNode?>> SilencedAsync(
        HttpClient client, string environment) =>
        [.. (await NoticeAsync(client, environment)).Where(one => one!["dismissedAt"] is not null)];

    private static async Task<string> TokenAsync(HttpClient human, params string[] scopes)
    {
        using var response = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "an agent token", scopes },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]!.GetValue<string>();
    }

    private static async Task<string> BoundTokenAsync(HttpClient human, string environment)
    {
        using var project = await human.GetAsync(Project, TestContext.Current.CancellationToken);

        project.EnsureSuccessStatusCode();

        var shape = JsonNode.Parse(await project.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        var one = shape["environments"]!.AsArray()
            .Single(each => each!["name"]!.GetValue<string>() == environment)!;

        using var issued = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new
            {
                kind = "agent",
                name = $"an agent kept inside {environment}",
                bindings = new[]
                {
                    new
                    {
                        projectId = shape["id"]!.GetValue<string>(),
                        environmentId = one["id"]!.GetValue<string>(),
                    },
                },
            },
            TestContext.Current.CancellationToken);

        issued.EnsureSuccessStatusCode();

        return JsonNode.Parse(await issued.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]!.GetValue<string>();
    }
}
