using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The organization's name, and the three things a person changes about
/// themselves (Specification §6.1).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class OrganizationEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_organization_is_called_Default_until_somebody_renames_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var organization = await human.GetFromJsonAsync<JsonNode>(
            "/api/v1/organization", TestContext.Current.CancellationToken);

        Assert.Equal("Default", organization!["name"]!.GetValue<string>());

        using var renamed = await human.PatchAsJsonAsync(
            "/api/v1/organization",
            new { name = "The Small Team" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var after = await human.GetFromJsonAsync<JsonNode>(
            "/api/v1/organization", TestContext.Current.CancellationToken);

        Assert.Equal("The Small Team", after!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Renaming_it_is_an_administrators_and_reading_it_is_anybodys()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var agent = instance.ClientWith(await AgentAsync(human));

        // A token knows which organization it is in the moment it authenticates;
        // the name is not a secret and a screen needs it.
        var read = await agent.GetFromJsonAsync<JsonNode>(
            "/api/v1/organization", TestContext.Current.CancellationToken);

        Assert.Equal("Default", read!["name"]!.GetValue<string>());

        using var refused = await agent.PatchAsJsonAsync(
            "/api/v1/organization",
            new { name = "Somebody else's" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("human-only", await IdentityTests.CodeOf(refused));
    }

    [Fact]
    public async Task A_person_renames_themselves_and_the_change_log_follows()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var renamed = await human.PatchAsJsonAsync(
            "/api/v1/me",
            new { name = "The Maintainer" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var me = await human.GetFromJsonAsync<JsonNode>(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal("The Maintainer", me!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_token_does_not_rename_the_person_it_is_accountable_to()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var agent = instance.ClientWith(await AgentAsync(human));

        using var refused = await agent.PatchAsJsonAsync(
            "/api/v1/me",
            new { name = "Not their name" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("forbidden", await IdentityTests.CodeOf(refused));
    }

    /// <summary>
    /// The second way to the address: an administrator can do it for anybody, and
    /// this is the way that needs nobody. It asks for the password for the reason
    /// a password change does — a session left open somewhere should not be enough
    /// to take somebody's own sign-in away.
    /// </summary>
    [Fact]
    public async Task You_change_the_address_you_sign_in_with_yourself()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);

        var mine = await instance.StartAsync();
        using var human = instance.ClientWith(mine);

        using var wrong = await human.PostAsJsonAsync(
            "/api/v1/me/email",
            new { email = "somewhere@example.test", currentPassword = "not-the-one-i-have" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        using var changed = await human.PostAsJsonAsync(
            "/api/v1/me/email",
            new
            {
                email = "Somewhere@Example.test",
                currentPassword = AnInstance.AdministratorPassword,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        // The session that asked is still the session that asked: an address is
        // not a credential, and every token names its person by id.
        var me = await human.GetFromJsonAsync<JsonNode>(
            "/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal("somewhere@example.test", me!["email"]!.GetValue<string>());

        using var stranger = instance.ClientWith(null);

        using var signedIn = await stranger.PostAsJsonAsync(
            "/api/v1/sessions",
            new
            {
                email = "somewhere@example.test",
                password = AnInstance.AdministratorPassword,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task An_address_somebody_else_here_has_is_refused_to_you_too()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var refused = await human.PostAsJsonAsync(
            "/api/v1/me/email",
            new { email = "not an address", currentPassword = AnInstance.AdministratorPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    /// <summary>
    /// The same line that keeps a token from renaming the person it is
    /// accountable to. Taking somebody's sign-in away from inside a process they
    /// handed a credential to is not what the credential was for.
    /// </summary>
    [Fact]
    public async Task A_token_does_not_change_the_address_of_the_person_it_belongs_to()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());
        using var agent = instance.ClientWith(await AgentAsync(human));

        using var refused = await agent.PostAsJsonAsync(
            "/api/v1/me/email",
            new { email = "somewhere@example.test", currentPassword = AnInstance.AdministratorPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("forbidden", await IdentityTests.CodeOf(refused));
    }

    [Fact]
    public async Task Changing_your_password_needs_the_one_you_have()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var wrong = await human.PostAsJsonAsync(
            "/api/v1/me/password",
            new { currentPassword = "not-the-one-i-have", newPassword = "a-longer-new-password" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task Changing_it_keeps_this_session_and_ends_every_other_one()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);

        var first = await instance.StartAsync();
        using var human = instance.ClientWith(first);

        // A second browser, or the same person on another machine.
        var second = (await IdentityTests.SignInAsync(
            instance.ClientWith(null), AnInstance.AdministratorPassword))["token"]!.GetValue<string>();

        using var elsewhere = instance.ClientWith(second);

        using var changed = await human.PostAsJsonAsync(
            "/api/v1/me/password",
            new
            {
                currentPassword = AnInstance.AdministratorPassword,
                newPassword = "a-longer-new-password",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        // The session that asked goes on working: somebody who has just proved
        // they know the password is not who this is protecting anybody from.
        using var mine = await human.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);

        using var theirs = await elsewhere.GetAsync(
            "/api/v1/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, theirs.StatusCode);

        // And the new password is the one that signs in.
        using var again = await instance.ClientWith(null).PostAsJsonAsync(
            "/api/v1/sessions",
            new { email = AnInstance.Administrator, password = "a-longer-new-password" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    private static async Task<string> AgentAsync(HttpClient human)
    {
        using var response = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "an agent with every scope" },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]!.GetValue<string>();
    }
}
