using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// What was done to people and to credentials, in the same log as what was done
/// to the vault (ADR 0020). Until these entries existed, an instance could not
/// answer the one question a change log has to: who changed the doors.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AdministrativeChangeLogTests(PostgresFixture postgres)
{
    /// <summary>
    /// The first entry an instance ever has. Every later entry about that person
    /// leans on it: a change of address records the <b>new</b> one because the
    /// old one is in the entry before it, and there has to be an entry before it.
    /// </summary>
    [Fact]
    public async Task The_first_run_is_the_first_entry_and_it_names_the_person_it_made()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var entries = await ChangesAsync(human);

        var joined = Assert.Single(entries, entry => Action(entry) is "joined")!;

        Assert.Equal(AnInstance.Administrator, joined["about"]!.GetValue<string>());
        Assert.Equal("human-session", joined["identity"]!["type"]!.GetValue<string>());

        // No place: it happened to a person and not to anything in the vault.
        Assert.Null(joined["project"]);
        Assert.Null(joined["environment"]);
        Assert.Null(joined["secret"]);
    }

    /// <summary>
    /// The sharpest entry in the log: afterwards a different person may sign in
    /// under the same name. Before this, the only record of it was in Postgres'
    /// own logs, which are not an answer for a human.
    /// </summary>
    [Fact]
    public async Task Every_administrative_act_on_a_person_is_recorded()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        var invited = await InviteAsync(human, "somebody@example.test");

        using var withdrawn = await human.DeleteAsync(
            $"/api/v1/invitations/{invited}", TestContext.Current.CancellationToken);

        withdrawn.EnsureSuccessStatusCode();

        var joined = await JoinAsync(instance, human, "newcomer@example.test");

        await PostAsync(human, $"/api/v1/users/{joined}/password", new { password = "another-long-password" });
        await PostAsync(human, $"/api/v1/users/{joined}/email", new { email = "moved@example.test" });
        await PostAsync(human, $"/api/v1/users/{joined}/deactivate", new { });
        await PostAsync(human, $"/api/v1/users/{joined}/reactivate", new { });

        var actions = (await ChangesAsync(human)).Select(Action).ToList();

        Assert.Equal(
            [
                "reactivated", "deactivated", "email-changed", "password-set",
                "joined", "invited", "invitation-withdrawn", "invited", "joined",
            ],
            actions);
    }

    /// <summary>
    /// A token is a key to the vault handed to a machine, and §6.4 makes issuing
    /// one human-only for that reason. A log that said nothing about it was
    /// missing the entry it most needed.
    /// </summary>
    [Fact]
    public async Task Issuing_and_revoking_a_token_are_in_the_log_by_name_and_never_by_value()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var issued = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new { kind = "agent", name = "quiet-otter-42" },
            TestContext.Current.CancellationToken);

        issued.EnsureSuccessStatusCode();

        var answered = JsonNode.Parse(await issued.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        var value = answered["value"]!.GetValue<string>();
        var id = answered["token"]!["id"]!.GetValue<string>();

        using var revoked = await human.DeleteAsync(
            $"/api/v1/tokens/{id}", TestContext.Current.CancellationToken);

        revoked.EnsureSuccessStatusCode();

        var log = await PageAsync(human);

        Assert.Equal(
            ["token-revoked", "token-created", "joined"],
            log["entries"]!.AsArray().Select(Action));

        Assert.All(
            log["entries"]!.AsArray().Where(entry => Action(entry).StartsWith("token-", StringComparison.Ordinal)),
            entry => Assert.Equal("quiet-otter-42", entry!["about"]!.GetValue<string>()));

        // The value exists once, in the answer to the request that made it. A
        // log line carrying it would be the one leak this product spends every
        // other decision avoiding (§6.5).
        Assert.DoesNotContain(value, log.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The organization's name, and a person's own name for themselves: both are
    /// what this log calls things, so a change to either belongs in it.
    /// </summary>
    [Fact]
    public async Task Renaming_the_organization_and_renaming_yourself_are_told_apart()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var organization = await human.PatchAsJsonAsync(
            "/api/v1/organization",
            new { name = "Datavision Zero" },
            TestContext.Current.CancellationToken);

        organization.EnsureSuccessStatusCode();

        using var me = await human.PatchAsJsonAsync(
            "/api/v1/me", new { name = "The Maintainer" }, TestContext.Current.CancellationToken);

        me.EnsureSuccessStatusCode();

        var entries = await ChangesAsync(human);

        var renamedOrganization = Assert.Single(entries, entry => Action(entry) is "organization-renamed")!;
        var renamedPerson = Assert.Single(entries, entry => Action(entry) is "person-renamed")!;

        Assert.Equal("Datavision Zero", renamedOrganization["about"]!.GetValue<string>());

        // A person is named in this log by the thing that did not change here.
        Assert.Equal(AnInstance.Administrator, renamedPerson["about"]!.GetValue<string>());
    }

    /// <summary>
    /// Changing your own password and having one set for you are the same action
    /// with a different actor, and the row already says which: the identity is on
    /// every entry. A second action word would have been the same fact twice.
    /// </summary>
    [Fact]
    public async Task A_person_changing_their_own_password_is_in_the_log_as_themselves()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        await PostAsync(
            human,
            "/api/v1/me/password",
            new
            {
                currentPassword = AnInstance.AdministratorPassword,
                newPassword = "a-different-long-password",
            });

        var entries = await ChangesAsync(human);
        var set = Assert.Single(entries, entry => Action(entry) is "password-set")!;

        Assert.Equal(AnInstance.Administrator, set["about"]!.GetValue<string>());
        Assert.Equal("Maintainer", set["identity"]!["name"]!.GetValue<string>());
    }

    /// <summary>
    /// No password, no token value, no invitation code — the rule §6.5 states for
    /// the vault, held to for people as well. What goes in <c>about</c> is an
    /// identifier: an address, a name.
    /// </summary>
    [Fact]
    public async Task No_credential_of_a_person_reaches_the_log()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var response = await human.PostAsJsonAsync(
            "/api/v1/invitations",
            new { email = "somebody@example.test", name = "Somebody", isAdministrator = false },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var written = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        var code = CodeIn(written["link"]!.GetValue<string>());

        await PostAsync(
            human,
            "/api/v1/me/password",
            new
            {
                currentPassword = AnInstance.AdministratorPassword,
                newPassword = "a-different-long-password",
            });

        var log = (await PageAsync(human)).ToJsonString();

        Assert.DoesNotContain(code, log, StringComparison.Ordinal);
        Assert.DoesNotContain("a-different-long-password", log, StringComparison.Ordinal);
        Assert.DoesNotContain(AnInstance.AdministratorPassword, log, StringComparison.Ordinal);

        // What is there is the address, which is an identifier and not a
        // credential.
        Assert.Contains("somebody@example.test", log, StringComparison.Ordinal);
    }

    /// <summary>
    /// §6.4 is unchanged for people: everybody in an organization sees everything
    /// in it, these entries included. For tokens it falls out stricter without a
    /// rule having to be written — administrative entries carry no project, so
    /// they are only ever in the unfiltered answer, which a bound token may not
    /// ask for.
    /// </summary>
    [Fact]
    public async Task A_bound_token_never_sees_what_was_done_to_a_person()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var human = instance.ClientWith(await instance.StartAsync());

        using var project = await human.PostAsJsonAsync(
            "/api/v1/projects", new { name = "webshop-api" }, TestContext.Current.CancellationToken);

        project.EnsureSuccessStatusCode();

        var catalogue = JsonNode.Parse(await project.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        using var issued = await human.PostAsJsonAsync(
            "/api/v1/tokens",
            new
            {
                kind = "service",
                name = "ci",
                bindings = new[] { new { projectId = catalogue["id"]!.GetValue<string>() } },
            },
            TestContext.Current.CancellationToken);

        issued.EnsureSuccessStatusCode();

        var bound = instance.ClientWith(JsonNode.Parse(await issued.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["value"]!.GetValue<string>());

        // The whole organization is what holds the administrative entries, and
        // that is exactly what a bound token may not ask for.
        using var everything = await bound.GetAsync(
            "/api/v1/changes", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, everything.StatusCode);

        // What it may ask for holds the project's entries and none of the others.
        using var narrowed = await bound.GetAsync(
            "/api/v1/changes?project=webshop-api", TestContext.Current.CancellationToken);

        narrowed.EnsureSuccessStatusCode();

        var entries = JsonNode.Parse(await narrowed.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["entries"]!.AsArray();

        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.Null(entry!["about"]));
        Assert.DoesNotContain(entries, entry => Action(entry) is "joined" or "token-created");

        bound.Dispose();
    }

    private static string Action(JsonNode? entry) => entry!["action"]!.GetValue<string>();

    /// <summary>
    /// The credential out of the link the answer carried. It lives in the
    /// fragment so that a working invitation never reaches an access log
    /// (ADR 0015), which is also why a test has to cut it out rather than being
    /// handed one.
    /// </summary>
    private static string CodeIn(string link) => link[(link.IndexOf('#', StringComparison.Ordinal) + 1)..];

    private static async Task<JsonNode> PageAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/api/v1/changes?limit=200", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonArray> ChangesAsync(HttpClient client) =>
        (await PageAsync(client))["entries"]!.AsArray();

    private static async Task PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(
            path, body, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>Writes an invitation and answers its id.</summary>
    private static async Task<string> InviteAsync(HttpClient human, string email)
    {
        using var response = await human.PostAsJsonAsync(
            "/api/v1/invitations",
            new { email, name = "Somebody", isAdministrator = false },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["invitation"]!["id"]!.GetValue<string>();
    }

    /// <summary>Invites somebody, accepts for them, and answers their user id.</summary>
    private static async Task<string> JoinAsync(
        AnInstance instance, HttpClient human, string email)
    {
        using var invited = await human.PostAsJsonAsync(
            "/api/v1/invitations",
            new { email, name = "Newcomer", isAdministrator = false },
            TestContext.Current.CancellationToken);

        invited.EnsureSuccessStatusCode();

        var code = CodeIn(JsonNode.Parse(await invited.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["link"]!.GetValue<string>());

        using var stranger = instance.ClientWith(null);

        using var accepted = await stranger.PostAsJsonAsync(
            "/api/v1/invitations/acceptance",
            new { code, name = "Newcomer", password = "a-password-of-real-length" },
            TestContext.Current.CancellationToken);

        accepted.EnsureSuccessStatusCode();

        return JsonNode.Parse(await accepted.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["userId"]!.GetValue<string>();
    }
}
