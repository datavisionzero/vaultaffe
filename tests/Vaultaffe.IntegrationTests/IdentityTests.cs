using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// Getting in (Specification §6.1, §6.3): the first run, a sign-in, and what a
/// token does and does not authenticate.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class IdentityTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_fresh_instance_says_it_has_not_been_started()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientWith(null);

        var shape = await client.GetFromJsonAsync<JsonNode>(
            "/api/v1/instance", TestContext.Current.CancellationToken);

        Assert.False(shape!["started"]!.GetValue<bool>());
        Assert.Null(shape["organizationName"]);
    }

    [Fact]
    public async Task The_first_run_makes_the_default_organization_and_an_administrator()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientWith(null);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/instance",
            new
            {
                email = "Maintainer@Example.test",
                name = "Maintainer",
                password = AnInstance.AdministratorPassword,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var started = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("Default", started["organizationName"]!.GetValue<string>());
        Assert.True(started["session"]!["isAdministrator"]!.GetValue<bool>());

        // Case is folded, because two spellings of one address are one person to
        // everybody except an index.
        Assert.Equal("maintainer@example.test", started["session"]!["email"]!.GetValue<string>());

        // A session token, and it reads as one before anything is looked up.
        Assert.StartsWith(
            "vaultaffe_session_", started["session"]!["token"]!.GetValue<string>(), StringComparison.Ordinal);

        var shape = await client.GetFromJsonAsync<JsonNode>(
            "/api/v1/instance", TestContext.Current.CancellationToken);

        Assert.True(shape!["started"]!.GetValue<bool>());
    }

    /// <summary>
    /// There is no second first run. Whoever reaches a fresh instance first owns
    /// it, and the second attempt is told so rather than quietly making another
    /// administrator.
    /// </summary>
    [Fact]
    public async Task An_instance_cannot_be_started_twice()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        using var again = await client.PostAsJsonAsync(
            "/api/v1/instance",
            new { email = "someone@example.test", name = "Someone", password = "another-long-password" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already-started", await CodeOf(again));
    }

    [Fact]
    public async Task A_password_under_the_rule_is_refused_by_name()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientWith(null);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/instance",
            new { email = "someone@example.test", name = "Someone", password = "short" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("validation", problem["code"]!.GetValue<string>());
        Assert.NotNull(problem["errors"]!["password"]);
    }

    [Fact]
    public async Task Signing_in_hands_over_a_session_and_me_says_who_it_is()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var anonymous = instance.ClientWith(null);

        var session = await SignInAsync(anonymous, AnInstance.AdministratorPassword);

        using var client = instance.ClientWith(session["token"]!.GetValue<string>());

        var me = await client.GetFromJsonAsync<JsonNode>(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal("Maintainer", me!["name"]!.GetValue<string>());
        Assert.Equal("session", me["tokenKind"]!.GetValue<string>());
        Assert.True(me["isAdministrator"]!.GetValue<bool>());

        // The scope set is words rather than the integer of flags the column
        // holds: a client should not have to know which bits those are.
        Assert.Equal(
            ["names", "read", "write", "delete"],
            me["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()));
    }

    /// <summary>
    /// A wrong password and an address nobody here has are the same answer. Which
    /// addresses belong to somebody is not a question a sign-in form should be
    /// able to answer.
    /// </summary>
    [Theory]
    [InlineData("maintainer@example.test", "the-wrong-password-entirely")]
    [InlineData("stranger@example.test", "a-password-of-real-length")]
    public async Task A_wrong_password_and_an_unknown_address_are_one_refusal(
        string email, string password)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/sessions",
            new { email, password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal("unauthenticated", problem["code"]!.GetValue<string>());
        Assert.Equal("That email address and password do not match.", problem["detail"]!.GetValue<string>());
    }

    /// <summary>
    /// A token that does not authenticate is refused rather than treated as an
    /// anonymous caller: being quietly nobody is how a client ends up debugging
    /// the wrong thing.
    /// </summary>
    [Theory]
    [InlineData("not-a-token")]
    [InlineData("vaultaffe_session_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_token_nothing_here_issued_is_refused(string presented)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(presented);

        using var response = await client.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", await CodeOf(response));
    }

    [Fact]
    public async Task A_request_with_no_token_at_all_is_refused_where_one_is_needed()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        using var response = await client.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_revokes_the_session_it_came_in_under()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        var token = await instance.StartAsync();

        using var client = instance.ClientWith(token);

        using var signedOut = await client.DeleteAsync(
            "/api/v1/sessions/current", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signedOut.StatusCode);

        var revoked = JsonNode.Parse(await signedOut.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.NotNull(revoked["revokedAt"]);

        using var afterwards = await client.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    internal static async Task<JsonNode> SignInAsync(HttpClient client, string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/sessions",
            new { email = AnInstance.Administrator, password },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    internal static async Task<string> CodeOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["code"]!.GetValue<string>();
}
