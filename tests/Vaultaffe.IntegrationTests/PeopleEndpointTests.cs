using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Vaultaffe.Domain.Identities;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The people of an organization (Specification §6.1): invitations that are
/// links, a reset an administrator performs, and what deactivating somebody
/// takes with it.
/// </summary>
/// <remarks>
/// The instance sends no email, so every one of these is something a person does
/// and hands over — which is why the tests below are about what an answer carries
/// exactly once, and about what stops working the moment somebody is out.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class PeopleEndpointTests(PostgresFixture postgres)
{
    private const string TheirPassword = "a-second-password-of-length";

    [Fact]
    public async Task An_invitation_is_a_link_that_appears_once_and_is_never_listed()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var written = await InviteAsync(human, "newcomer@example.test", "Newcomer");

        var link = written["link"]!.GetValue<string>();

        // Relative, with the code in the fragment: the instance does not know
        // what address a browser reached it at, and a fragment never leaves the
        // browser — so no access log holds a working invitation.
        Assert.StartsWith("/invite#", link, StringComparison.Ordinal);
        Assert.Equal("open", written["invitation"]!["state"]!.GetValue<string>());

        var listed = await human.GetFromJsonAsync<JsonNode>(
            "/api/v1/invitations", TestContext.Current.CancellationToken);

        var one = listed!.AsArray().Single()!;

        Assert.Equal("newcomer@example.test", one["email"]!.GetValue<string>());
        Assert.Null(one["link"]);
        Assert.Null(one["code"]);
    }

    [Fact]
    public async Task Somebody_holding_the_link_reads_what_it_is_for_and_accepts_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var code = CodeOf(await InviteAsync(human, "newcomer@example.test", "Newcomer"));

        // No token: the person holding a link is exactly somebody this instance
        // has never met.
        using var stranger = instance.ClientWith(null);

        var offer = await OfferAsync(stranger, code);

        Assert.Equal("Default", offer["organizationName"]!.GetValue<string>());
        Assert.Equal("newcomer@example.test", offer["email"]!.GetValue<string>());
        Assert.Equal("open", offer["state"]!.GetValue<string>());

        var session = await AcceptAsync(stranger, code, "Newcomer of theirs", TheirPassword);

        Assert.False(session["isAdministrator"]!.GetValue<bool>());
        Assert.StartsWith(
            "vaultaffe_session_", session["token"]!.GetValue<string>(), StringComparison.Ordinal);

        using var theirs = instance.ClientWith(session["token"]!.GetValue<string>());

        var me = await theirs.GetFromJsonAsync<JsonNode>(
            "/api/v1/me", TestContext.Current.CancellationToken);

        // The name is theirs to spell; the address and the administrator line are
        // the invitation's and not the request's.
        Assert.Equal("Newcomer of theirs", me!["name"]!.GetValue<string>());
        Assert.False(me["isAdministrator"]!.GetValue<bool>());
    }

    [Fact]
    public async Task An_invitation_works_exactly_once()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var stranger = instance.ClientWith(null);

        var code = CodeOf(await InviteAsync(human, "newcomer@example.test", "Newcomer"));

        await AcceptAsync(stranger, code, "Newcomer", TheirPassword);

        using var again = await stranger.PostAsJsonAsync(
            "/api/v1/invitations/acceptance",
            new { code, name = "Somebody else", password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Equal("accepted", (await OfferAsync(stranger, code))["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_withdrawn_invitation_stops_working()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var stranger = instance.ClientWith(null);

        var written = await InviteAsync(human, "newcomer@example.test", "Newcomer");
        var code = CodeOf(written);

        using var withdrawn = await human.DeleteAsync(
            $"/api/v1/invitations/{written["invitation"]!["id"]!.GetValue<Guid>()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);

        using var refused = await stranger.PostAsJsonAsync(
            "/api/v1/invitations/acceptance",
            new { code, name = (string?)null, password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task An_invitation_nobody_used_in_time_stops_working()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var stranger = instance.ClientWith(null);

        var code = CodeOf(await InviteAsync(human, "newcomer@example.test", "Newcomer"));

        instance.Clock.MoveOn(Invitation.Lifetime + TimeSpan.FromMinutes(1));

        Assert.Equal("expired", (await OfferAsync(stranger, code))["state"]!.GetValue<string>());

        using var refused = await stranger.PostAsJsonAsync(
            "/api/v1/invitations/acceptance",
            new { code, name = (string?)null, password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_code_this_instance_never_wrote_is_an_invitation_to_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var stranger = instance.ClientWith(null);

        using var response = await stranger.PostAsJsonAsync(
            "/api/v1/invitations/offer",
            new { code = "not-a-code-anybody-here-issued" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_address_that_already_signs_in_here_is_not_invited_again()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var response = await human.PostAsJsonAsync(
            "/api/v1/invitations",
            new { email = AnInstance.Administrator, name = "The same person" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("name-taken", await IdentityTests.CodeOf(response));
    }

    [Fact]
    public async Task One_open_invitation_per_address_and_no_more()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await InviteAsync(human, "newcomer@example.test", "Newcomer");

        using var second = await human.PostAsJsonAsync(
            "/api/v1/invitations",
            new { email = "newcomer@example.test", name = "Newcomer" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Inviting_is_an_administrators_and_a_persons()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        // An agent carrying every scope there is: being human-only is not a
        // permission a token can be given (§6.4).
        using var agent = instance.ClientWith(await AgentAsync(human));

        using var refused = await agent.PostAsJsonAsync(
            "/api/v1/invitations",
            new { email = "newcomer@example.test", name = "Newcomer" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("human-only", await IdentityTests.CodeOf(refused));

        // And a person of the organization who does not administer it is told
        // that they do not, because their remedy is to ask somebody who does.
        using var theirs = instance.ClientWith(await JoinAsync(instance, human));

        using var byAMember = await theirs.PostAsJsonAsync(
            "/api/v1/invitations",
            new { email = "another@example.test", name = "Another" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, byAMember.StatusCode);
        Assert.Equal("forbidden", await IdentityTests.CodeOf(byAMember));
    }

    [Fact]
    public async Task A_reset_hands_over_a_new_password_and_ends_every_session_of_theirs()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var token = await JoinAsync(instance, human);
        using var theirs = instance.ClientWith(token);

        var them = (await human.GetFromJsonAsync<JsonNode>(
            "/api/v1/users", TestContext.Current.CancellationToken))!
            .AsArray()
            .Single(user => user!["email"]!.GetValue<string>() == "newcomer@example.test")!;

        using var reset = await human.PostAsJsonAsync(
            $"/api/v1/users/{them["id"]!.GetValue<Guid>()}/password",
            new { password = "the-password-an-administrator-set" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // The session opened with the old password is gone with it.
        using var afterwards = await theirs.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);

        using var stranger = instance.ClientWith(null);

        using var signedIn = await stranger.PostAsJsonAsync(
            "/api/v1/sessions",
            new
            {
                email = "newcomer@example.test",
                password = "the-password-an-administrator-set",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task Deactivating_somebody_takes_every_token_of_theirs_with_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var theirs = instance.ClientWith(await JoinAsync(instance, human));

        // A token they are accountable for, still running somewhere.
        var theirAgent = await AgentAsync(theirs);
        using var agent = instance.ClientWith(theirAgent);

        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken)).StatusCode);

        var id = await IdOfAsync(human, "newcomer@example.test");

        using var deactivated = await human.PostAsync(
            $"/api/v1/users/{id}/deactivate", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

        using var theirSession = await theirs.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, theirSession.StatusCode);

        // The agent token they are accountable for goes with them: a person who
        // is out of the organization does not go on acting in it through
        // something they left running.
        using var theirAgentAfterwards = await agent.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, theirAgentAfterwards.StatusCode);

        using var signIn = await instance.ClientWith(null).PostAsJsonAsync(
            "/api/v1/sessions",
            new { email = "newcomer@example.test", password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);

        using var reactivated = await human.PostAsync(
            $"/api/v1/users/{id}/reactivate", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);

        using var again = await instance.ClientWith(null).PostAsJsonAsync(
            "/api/v1/sessions",
            new { email = "newcomer@example.test", password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Nobody_deactivates_themselves()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var me = await human.GetFromJsonAsync<JsonNode>(
            "/api/v1/me", TestContext.Current.CancellationToken);

        using var refused = await human.PostAsync(
            $"/api/v1/users/{me!["userId"]!.GetValue<Guid>()}/deactivate",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("forbidden", await IdentityTests.CodeOf(refused));
    }

    [Fact]
    public async Task The_listing_says_who_is_here_and_nothing_about_their_password()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await JoinAsync(instance, human);

        var listed = (await human.GetFromJsonAsync<JsonNode>(
            "/api/v1/users", TestContext.Current.CancellationToken))!.AsArray();

        Assert.Equal(2, listed.Count);

        foreach (var user in listed)
        {
            Assert.Null(user!["passwordHash"]);
            Assert.Null(user["password"]);
            Assert.NotNull(user["email"]);
        }

        // Oldest first: the first user of an instance is its administrator, and
        // a list of people reads as the order they arrived in.
        Assert.Equal(AnInstance.Administrator, listed[0]!["email"]!.GetValue<string>());
        Assert.True(listed[0]!["isAdministrator"]!.GetValue<bool>());
        Assert.Null(listed[0]!["deactivatedAt"]?.GetValue<DateTimeOffset?>());
    }

    /// <summary>An invitation, written out by an administrator.</summary>
    private static async Task<JsonNode> InviteAsync(HttpClient human, string email, string name)
    {
        using var response = await human.PostAsJsonAsync(
            "/api/v1/invitations",
            new { email, name },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    /// <summary>The code out of the one answer that carries it.</summary>
    private static string CodeOf(JsonNode written) =>
        written["link"]!.GetValue<string>().Split('#')[1];

    private static async Task<JsonNode> OfferAsync(HttpClient client, string code)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/invitations/offer",
            new { code },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonNode> AcceptAsync(
        HttpClient client, string code, string? name, string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/invitations/acceptance",
            new { code, name, password },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    /// <summary>Somebody who is in the organization and does not administer it.</summary>
    private static async Task<string> JoinAsync(AnInstance instance, HttpClient human)
    {
        var code = CodeOf(await InviteAsync(human, "newcomer@example.test", "Newcomer"));

        using var stranger = instance.ClientWith(null);

        return (await AcceptAsync(stranger, code, "Newcomer", TheirPassword))["token"]!
            .GetValue<string>();
    }

    private static async Task<Guid> IdOfAsync(HttpClient human, string email) =>
        (await human.GetFromJsonAsync<JsonNode>(
            "/api/v1/users", TestContext.Current.CancellationToken))!
            .AsArray()
            .Single(user => user!["email"]!.GetValue<string>() == email)!["id"]!
            .GetValue<Guid>();

    private static async Task<string> AgentAsync(HttpClient human)
    {
        using var response = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "an agent of theirs" },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]!.GetValue<string>();
    }
}
