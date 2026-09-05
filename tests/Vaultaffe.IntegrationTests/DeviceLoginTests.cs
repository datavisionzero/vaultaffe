using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The device-code login of Specification §6.2 — the one that works where there
/// is no browser on the machine asking: an SSH session, a CI job, a container, an
/// agent's sandbox.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DeviceLoginTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Beginning_a_login_hands_over_a_code_for_the_human_and_one_for_the_client()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        var begun = await BeginAsync(client);

        // The short one is what a person reads out of a terminal: eight
        // characters with a dash in the middle, and no vowel to make a word of.
        var userCode = begun["userCode"]!.GetValue<string>();

        Assert.Matches("^[BCDFGHJKLMNPQRSTVWXZ]{4}-[BCDFGHJKLMNPQRSTVWXZ]{4}$", userCode);

        // The long one is the credential, and it is nothing a person is shown.
        Assert.True(begun["deviceCode"]!.GetValue<string>().Length >= 40);
        Assert.NotEqual(userCode, begun["deviceCode"]!.GetValue<string>());

        Assert.Equal("/device", begun["verificationUri"]!.GetValue<string>());
        Assert.Equal($"/device?code={userCode}", begun["verificationUriComplete"]!.GetValue<string>());
        Assert.Equal(600, begun["expiresInSeconds"]!.GetValue<int>());
        Assert.Equal(5, begun["intervalSeconds"]!.GetValue<int>());
    }

    [Fact]
    public async Task Polling_before_anybody_confirms_says_exactly_that()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        var begun = await BeginAsync(client);

        using var polled = await PollAsync(client, begun["deviceCode"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.BadRequest, polled.StatusCode);
        Assert.Equal("device-pending", await IdentityTests.CodeOf(polled));
    }

    [Fact]
    public async Task A_human_confirms_in_a_browser_and_the_next_poll_gets_the_session()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        var begun = await BeginAsync(client);
        var userCode = begun["userCode"]!.GetValue<string>();

        using var confirmed = await ConfirmAsync(client, userCode, AnInstance.AdministratorPassword);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Contains(
            "Confirmed",
            await confirmed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        using var polled = await PollAsync(client, begun["deviceCode"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.OK, polled.StatusCode);

        var session = JsonNode.Parse(await polled.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.StartsWith(
            "vaultaffe_session_", session["token"]!.GetValue<string>(), StringComparison.Ordinal);

        // And it is a session that works.
        using var signedIn = instance.ClientWith(session["token"]!.GetValue<string>());

        using var me = await signedIn.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    /// <summary>
    /// A device code works once. One left behind in a CI log is worth nothing to
    /// whoever finds it, because the token it was for has already been collected.
    /// </summary>
    [Fact]
    public async Task A_device_code_hands_over_one_token_and_never_a_second()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        var begun = await BeginAsync(client);
        var deviceCode = begun["deviceCode"]!.GetValue<string>();

        using var confirmed = await ConfirmAsync(
            client, begun["userCode"]!.GetValue<string>(), AnInstance.AdministratorPassword);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        using var first = await PollAsync(client, deviceCode);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await PollAsync(client, deviceCode);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("device-expired", await IdentityTests.CodeOf(second));
    }

    [Fact]
    public async Task A_human_who_did_not_start_it_refuses_it_and_the_poll_stops()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        var begun = await BeginAsync(client);

        using var denied = await ConfirmAsync(
            client, begun["userCode"]!.GetValue<string>(), AnInstance.AdministratorPassword, deny: true);

        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.Contains(
            "Refused",
            await denied.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        using var polled = await PollAsync(client, begun["deviceCode"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.BadRequest, polled.StatusCode);
        Assert.Equal("device-denied", await IdentityTests.CodeOf(polled));
    }

    /// <summary>
    /// Guessing the short code is not enough, which is what lets the short code be
    /// short: confirming takes the password of somebody who is actually here.
    /// </summary>
    [Fact]
    public async Task Confirming_takes_a_password_and_a_wrong_one_leaves_the_login_pending()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        var begun = await BeginAsync(client);

        using var refused = await ConfirmAsync(
            client, begun["userCode"]!.GetValue<string>(), "the-wrong-password-entirely");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var page = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("do not match", page, StringComparison.Ordinal);

        using var polled = await PollAsync(client, begun["deviceCode"]!.GetValue<string>());

        Assert.Equal("device-pending", await IdentityTests.CodeOf(polled));
    }

    [Fact]
    public async Task A_device_code_nothing_here_issued_is_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        using var polled = await PollAsync(client, "not-a-device-code-anybody-issued");

        Assert.Equal(HttpStatusCode.NotFound, polled.StatusCode);
        Assert.Equal("not-found", await IdentityTests.CodeOf(polled));
    }

    /// <summary>
    /// The page is a page for a person: it renders as HTML, and the code the CLI
    /// put in the link is already in the form.
    /// </summary>
    [Fact]
    public async Task The_confirmation_page_renders_with_the_code_already_in_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await instance.StartAsync();

        using var client = instance.ClientWith(null);

        var begun = await BeginAsync(client);
        var userCode = begun["userCode"]!.GetValue<string>();

        using var page = await client.GetAsync(
            $"/device?code={userCode}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);

        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains($"value=\"{userCode}\"", html, StringComparison.Ordinal);
        Assert.Contains("Confirm a sign-in", html, StringComparison.Ordinal);
    }

    private static async Task<JsonNode> BeginAsync(HttpClient client)
    {
        using var response = await client.PostAsync(
            "/api/v1/device/authorizations", content: null, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    private static Task<HttpResponseMessage> PollAsync(HttpClient client, string deviceCode) =>
        client.PostAsJsonAsync(
            "/api/v1/device/tokens",
            new { deviceCode },
            TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client, string userCode, string password, bool deny = false) =>
        client.PostAsync(
            "/device",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = userCode,
                ["email"] = AnInstance.Administrator,
                ["password"] = password,
                [deny ? "deny" : "approve"] = "1",
            }),
            TestContext.Current.CancellationToken);
}
