using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// A machine asks for a token of its own (ADR 0021): the device-code flow, ending
/// in an agent token rather than in a person's session.
/// </summary>
/// <remarks>
/// The property every one of these is really about is that <b>the value never
/// crosses a person</b>. It is answered to the machine that asked and to nothing
/// else — not to the screen where somebody agreed, not to a terminal, and so not
/// to a scrollback or a transcript.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class EnrollmentEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_person_agrees_and_the_machine_that_asked_collects_the_token()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var machine = instance.ClientAnnouncing(null);

        using var project = await human.PostAsJsonAsync(
            "/api/v1/projects", new { name = "webshop-api" }, TestContext.Current.CancellationToken);

        project.EnsureSuccessStatusCode();

        var catalogue = JsonNode.Parse(await project.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        var begun = await BeginAsync(machine, "claude in ~/webshop");
        var code = begun["userCode"]!.GetValue<string>();

        // What the person is shown before they decide: what the machine says it
        // is, and nothing the instance cannot stand behind.
        using var asked = await human.GetAsync(
            $"/api/v1/enrollments/{code}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);

        var waiting = JsonNode.Parse(await asked.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("claude in ~/webshop", waiting["requestedName"]!.GetValue<string>());
        Assert.Equal("pending", waiting["state"]!.GetValue<string>());

        // Until somebody decides, the poll says exactly one thing, and it is the
        // one a client keeps asking on.
        using var early = await machine.PostAsJsonAsync(
            "/api/v1/enrollments/tokens",
            new { deviceCode = begun["deviceCode"]!.GetValue<string>() },
            TestContext.Current.CancellationToken);

        Assert.Equal("device-pending", await IdentityTests.CodeOf(early));

        // The person settles the name, narrows the scopes and binds it.
        using var approved = await human.PostAsJsonAsync(
            $"/api/v1/enrollments/{code}/approval",
            new
            {
                name = "the agent on the webshop",
                scopes = new[] { "names", "read" },
                bindings = new[] { new { projectId = catalogue["id"]!.GetValue<string>() } },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        using var collected = await machine.PostAsJsonAsync(
            "/api/v1/enrollments/tokens",
            new { deviceCode = begun["deviceCode"]!.GetValue<string>() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, collected.StatusCode);

        var issued = JsonNode.Parse(await collected.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        var token = issued["token"]!;

        Assert.Equal("agent", token["kind"]!.GetValue<string>());
        Assert.Equal("the agent on the webshop", token["name"]!.GetValue<string>());
        Assert.Equal(
            ["names", "read"],
            token["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()));
        Assert.False(token["reachesTheWholeOrganization"]!.GetValue<bool>());
        Assert.Single(token["bindings"]!.AsArray());

        // And it is a token: the machine that asked can now act under it, as
        // itself.
        using var agent = instance.ClientWith(issued["value"]!.GetValue<string>());

        using var me = await agent.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var caller = JsonNode.Parse(await me.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("agent", caller["tokenKind"]!.GetValue<string>());
        Assert.Equal("the agent on the webshop", caller["tokenName"]!.GetValue<string>());
    }

    /// <summary>
    /// The entry says a person handed a machine a key, because that is what
    /// happened. Recorded under the token being collected it would read as a
    /// machine handing itself one, which is the sentence the identity type exists
    /// to keep out of this log (§6.4, §6.5).
    /// </summary>
    [Fact]
    public async Task The_change_log_says_the_person_who_agreed_created_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var machine = instance.ClientAnnouncing(null);

        var begun = await BeginAsync(machine, "an agent somewhere");

        await ApproveAsync(human, begun, name: "an agent somewhere");
        await CollectAsync(machine, begun);

        using var changes = await human.GetAsync(
            "/api/v1/changes", TestContext.Current.CancellationToken);

        var entry = JsonNode.Parse(await changes.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["entries"]!.AsArray()
            .Single(one => one!["about"]?.GetValue<string>() == "an agent somewhere")!;

        Assert.Equal("token-created", entry["action"]!.GetValue<string>());
        Assert.Equal("human-session", entry["identity"]!["type"]!.GetValue<string>());
    }

    /// <summary>
    /// A device code works once, for the reason a login's does: one that stayed
    /// usable after handing a token over would be a second credential for
    /// whoever still had it.
    /// </summary>
    [Fact]
    public async Task The_token_is_collected_once_and_the_code_is_worth_nothing_after()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var machine = instance.ClientAnnouncing(null);

        var begun = await BeginAsync(machine, "an agent somewhere");

        await ApproveAsync(human, begun);
        await CollectAsync(machine, begun);

        using var again = await machine.PostAsJsonAsync(
            "/api/v1/enrollments/tokens",
            new { deviceCode = begun["deviceCode"]!.GetValue<string>() },
            TestContext.Current.CancellationToken);

        Assert.Equal("device-expired", await IdentityTests.CodeOf(again));
    }

    [Fact]
    public async Task A_person_can_say_they_did_not_start_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var machine = instance.ClientAnnouncing(null);

        var begun = await BeginAsync(machine, "something nobody started");

        using var refused = await human.PostAsync(
            $"/api/v1/enrollments/{begun["userCode"]!.GetValue<string>()}/refusal",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);

        using var polled = await machine.PostAsJsonAsync(
            "/api/v1/enrollments/tokens",
            new { deviceCode = begun["deviceCode"]!.GetValue<string>() },
            TestContext.Current.CancellationToken);

        Assert.Equal("device-denied", await IdentityTests.CodeOf(polled));
    }

    /// <summary>
    /// Agreeing is human-only with <c>create-token</c>, because what comes out of
    /// it is a token. An agent that could agree to one for itself would be a
    /// credential nobody issued.
    /// </summary>
    [Fact]
    public async Task An_agent_may_not_agree_to_an_enrollment()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var machine = instance.ClientAnnouncing(null);

        using var created = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "the agent in this terminal" },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        using var agent = instance.ClientWith(JsonNode.Parse(
            await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!
            ["value"]!.GetValue<string>());

        var begun = await BeginAsync(machine, "a second agent");

        using var response = await agent.PostAsJsonAsync(
            $"/api/v1/enrollments/{begun["userCode"]!.GetValue<string>()}/approval",
            new { name = "a second agent" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("human-only", await IdentityTests.CodeOf(response));

        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("create-token", problem["humanAction"]!.GetValue<string>());
    }

    /// <summary>
    /// Reading one needs a session. It is not a credential, but it says what is
    /// happening inside somebody's organization, and an unauthenticated reader is
    /// nobody.
    /// </summary>
    [Fact]
    public async Task Nobody_reads_an_enrollment_without_a_session()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        _ = await instance.StartAsync();
        using var machine = instance.ClientAnnouncing(null);

        var begun = await BeginAsync(machine, "an agent somewhere");

        using var response = await machine.GetAsync(
            $"/api/v1/enrollments/{begun["userCode"]!.GetValue<string>()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Two flows through one table, and a code belongs to the one it was issued
    /// for. Polling a login here — or an enrollment at the login's endpoint — is
    /// nothing found, which is also the only answer that does not tell whoever
    /// asked which codes exist.
    /// </summary>
    [Fact]
    public async Task A_login_is_not_an_enrollment_and_neither_is_the_other_way_round()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var machine = instance.ClientAnnouncing(null);

        using var login = await machine.PostAsync(
            "/api/v1/device/authorizations", null, TestContext.Current.CancellationToken);

        login.EnsureSuccessStatusCode();

        var begunLogin = JsonNode.Parse(await login.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        using var collectedAsEnrollment = await machine.PostAsJsonAsync(
            "/api/v1/enrollments/tokens",
            new { deviceCode = begunLogin["deviceCode"]!.GetValue<string>() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, collectedAsEnrollment.StatusCode);

        // And a login's short code is not one this screen decides either.
        using var read = await human.GetAsync(
            $"/api/v1/enrollments/{begunLogin["userCode"]!.GetValue<string>()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        var begunEnrollment = await BeginAsync(machine, "an agent somewhere");

        using var redeemedAsLogin = await machine.PostAsJsonAsync(
            "/api/v1/device/tokens",
            new { deviceCode = begunEnrollment["deviceCode"]!.GetValue<string>() },
            TestContext.Current.CancellationToken);

        Assert.Equal("device-pending", await IdentityTests.CodeOf(redeemedAsLogin));
    }

    [Fact]
    public async Task An_enrollment_needs_a_name_for_the_person_who_will_read_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        _ = await instance.StartAsync();
        using var machine = instance.ClientAnnouncing(null);

        using var response = await machine.PostAsJsonAsync(
            "/api/v1/enrollments", new { name = "  " }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await IdentityTests.CodeOf(response));
    }

    private static async Task<JsonNode> BeginAsync(HttpClient machine, string name)
    {
        using var response = await machine.PostAsJsonAsync(
            "/api/v1/enrollments", new { name }, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task ApproveAsync(
        HttpClient human, JsonNode begun, string? name = null)
    {
        using var response = await human.PostAsJsonAsync(
            $"/api/v1/enrollments/{begun["userCode"]!.GetValue<string>()}/approval",
            new { name },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonNode> CollectAsync(HttpClient machine, JsonNode begun)
    {
        using var response = await machine.PostAsJsonAsync(
            "/api/v1/enrollments/tokens",
            new { deviceCode = begun["deviceCode"]!.GetValue<string>() },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }
}
