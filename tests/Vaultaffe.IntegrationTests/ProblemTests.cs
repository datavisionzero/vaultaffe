using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vaultaffe.IntegrationTests;

/// <summary>
/// The error shape: every refusal is a problem document with a code a client can
/// switch on, and the <c>type</c> that names it resolves to something this
/// instance actually serves.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ProblemTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Every_code_in_the_catalogue_is_documented_where_its_type_points()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing(null);

        var catalogue = await client.GetFromJsonAsync<JsonArray>(
            "/problems", TestContext.Current.CancellationToken);

        Assert.NotEmpty(catalogue!);

        foreach (var kind in catalogue!)
        {
            var code = kind!["code"]!.GetValue<string>();

            // The type of a problem document is /problems/<code>, resolved
            // against the request. Following it has to arrive somewhere.
            var described = await client.GetFromJsonAsync<JsonNode>(
                $"/problems/{code}", TestContext.Current.CancellationToken);

            Assert.Equal(code, described!["code"]!.GetValue<string>());
            Assert.Equal(kind["status"]!.GetValue<int>(), described["status"]!.GetValue<int>());
            Assert.NotEmpty(described["title"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task A_code_this_instance_does_not_raise_is_a_problem_document_itself()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.ClientAnnouncing(null);

        using var response = await client.GetAsync(
            "/problems/no-such-refusal", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("not-found", problem["code"]!.GetValue<string>());
        Assert.Equal("/problems/not-found", problem["type"]!.GetValue<string>());
    }
}
