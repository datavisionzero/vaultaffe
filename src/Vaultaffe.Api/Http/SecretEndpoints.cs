using Vaultaffe.Application.Acts;
using Vaultaffe.Domain.Authorization;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Api.Http;

/// <summary>
/// A value to write, or the placeholder that says a human still has to.
/// </summary>
/// <param name="Value">The value, exactly as it should be stored. Null asks for an empty placeholder.</param>
/// <param name="Replace">Required to write over a key that already holds a value. Filling a placeholder does not need it.</param>
public sealed record SetSecretRequest(string? Value = null, bool Replace = false);

/// <summary>A <c>.env</c> file to take in.</summary>
/// <param name="Content">The file itself.</param>
/// <param name="Replace">Whether keys that already hold a value may be written over.</param>
public sealed record ImportRequest(string Content, bool Replace = false);

/// <summary>A secret as the listing shows it — the name, and whether anything is in it.</summary>
public sealed record SecretShape(
    Guid Id,
    string Name,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValueWrittenAt,
    DateTimeOffset? DeletedAt);

/// <summary>One secret and its value. Null when it is an empty placeholder.</summary>
public sealed record SecretValueShape(SecretShape Secret, string? Value);

/// <summary>A setting an import did not apply, and why. The reason names no value.</summary>
public sealed record SkippedShape(string Name, string Reason);

/// <summary>A line the file could not be read as a setting at.</summary>
public sealed record UnreadableShape(int Line, string Reason);

/// <summary>What an import did, by name.</summary>
public sealed record ImportShape(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Filled,
    IReadOnlyList<string> Replaced,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<SkippedShape> Skipped,
    IReadOnlyList<UnreadableShape> Unreadable);

/// <summary>
/// The secrets surface (<c>docs/api.md</c>) — the actual product, server-side.
/// </summary>
/// <remarks>
/// <b>Two endpoints, two scopes, and the difference between them is the whole
/// design.</b> The listing returns names and status and is bound to
/// <c>names</c>; it is the normal case for an agent and exactly the shape a later
/// MCP server needs. Reading a value is a second endpoint, one key at a time,
/// bound to <c>read</c> — the dangerous scope in a secrets manager, and the one
/// an agent token can be issued without.
/// <para>
/// Everything the CLI can do is here, because a later MCP server has to be a thin
/// shell over this rather than a second way into the data (§8).
/// </para>
/// </remarks>
public static class SecretEndpoints
{
    private const string Tag = "Secrets";

    private const string Under = "/{project}/environments/{environment}/secrets";

    /// <summary>
    /// Import and export hang off the environment rather than off
    /// <c>secrets/</c>, and that is a routing decision rather than a taste one: a
    /// literal beats a parameter and route matching ignores case, so
    /// <c>secrets/export</c> beside <c>secrets/{name}</c> would shadow a secret
    /// somebody called <c>EXPORT</c>. They are also what they say — a whole
    /// environment in, a whole environment out.
    /// </summary>
    private const string Whole = "/{project}/environments/{environment}";

    public static IEndpointRouteBuilder MapSecrets(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup($"{ApiVersion.Route}/projects").WithTags(Tag);

        api.MapGet(Under, async (
                string project,
                string environment,
                bool? deleted,
                ListSecrets list,
                CancellationToken cancellation) =>
                (await list.ExecuteAsync(project, environment, deleted ?? false, cancellation))
                    .Select(Vaulted.Secret).ToList())
            .Needing(Scopes.Names)
            .WithName("ReadSecretNames")
            .WithSummary("Every key and whether it is set, never a value. `deleted=true` lists the recoverable ones.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet($"{Under}/{{name}}", async (
                string project,
                string environment,
                string name,
                ReadSecret read,
                CancellationToken cancellation) =>
                Vaulted.Value(await read.ExecuteAsync(project, environment, name, cancellation)))
            .Needing(Scopes.Read)
            .WithName("ReadSecret")
            .WithSummary("One value, by name. The only answer here that carries one.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPut($"{Under}/{{name}}", async (
                string project,
                string environment,
                string name,
                SetSecretRequest request,
                SetSecret set,
                CancellationToken cancellation) =>
                Vaulted.Secret(await set.ExecuteAsync(
                    project, environment, name, request.Value, request.Replace, cancellation)))
            .Needing(Scopes.Write)
            .WithName("SetSecret")
            .WithSummary("Write a value, or create an empty placeholder. The answer never echoes it.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete($"{Under}/{{name}}", async (
                string project,
                string environment,
                string name,
                DeleteSecret delete,
                CancellationToken cancellation) =>
                Vaulted.Secret(await delete.ExecuteAsync(
                    project, environment, name, cancellation)))
            .Needing(Scopes.Delete)
            .WithName("DeleteSecret")
            .WithSummary("Delete a secret recoverably.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost($"{Under}/{{name}}/restore", async (
                string project,
                string environment,
                string name,
                RestoreSecret restore,
                CancellationToken cancellation) =>
                Results.Ok(Vaulted.Secret(await restore.ExecuteAsync(
                    project, environment, name, cancellation))))
            .Needing(Scopes.Delete)
            .WithName("RestoreSecret")
            .WithSummary("Bring a deleted secret back inside its recovery window.")
            .Produces<SecretShape>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone);

        api.MapPost($"{Whole}/import", async (
                string project,
                string environment,
                ImportRequest request,
                ImportSecrets import,
                CancellationToken cancellation) =>
                Results.Ok(Vaulted.Import(await import.ExecuteAsync(
                    project, environment, request.Content, request.Replace, cancellation))))
            .Needing(Scopes.Write)
            .WithName("ImportSecrets")
            .WithSummary("Take a .env in, all of it or none of it. The answer names keys only.")
            .Produces<ImportShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost($"{Under}/{{name}}/purge", async (
                string project,
                string environment,
                string name,
                PurgeSecret purge,
                CancellationToken cancellation) =>
                Results.Ok(await purge.ExecuteAsync(project, environment, name, cancellation)))
            .Needing(Scopes.Delete)
            .HumanOnly(HumanAction.Purge)
            .WithName("PurgeSecret")
            .WithSummary("Remove a deleted secret and everything it held, now. Human sessions only.")
            .Produces<Purged>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // The headline case of §6.5: after a suspected compromise, a key's
        // history genuinely disappears. Neither Doppler nor AWS offers that
        // cleanly; here it is one request.
        api.MapDelete($"{Under}/{{name}}/versions", async (
                string project,
                string environment,
                string name,
                PurgeValueHistory purge,
                CancellationToken cancellation) =>
                await purge.ExecuteAsync(project, environment, name, cancellation))
            .Needing(Scopes.Delete)
            .HumanOnly(HumanAction.Purge)
            .WithName("PurgeSecretVersions")
            .WithSummary("Remove what this secret used to hold, keeping it. Human sessions only.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Everything, in plaintext, and therefore only for a person. It is the
        // contradiction `inject` was rejected for (§7) and it stays, because a
        // way back out is part of being trustworthy — behind the one restriction
        // that makes that defensible.
        api.MapGet($"{Whole}/export", async (
                string project,
                string environment,
                ExportEnvironment export,
                CancellationToken cancellation) =>
                Results.Text(
                    await export.ExecuteAsync(project, environment, cancellation),
                    "text/plain"))
            .Needing(Scopes.Read)
            .HumanOnly(HumanAction.Export)
            .WithName("ExportEnvironment")
            .WithSummary("Every value of an environment as a .env. Human sessions only.")
            .Produces<string>(StatusCodes.Status200OK, "text/plain")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

/// <summary>The vault's rows, as the contract shows them.</summary>
public static class Vaulted
{
    /// <summary>
    /// <c>set</c> or <c>empty</c> — the two states Specification §6.2 names. A
    /// word rather than a boolean called <c>isPlaceholder</c>, because the CLI
    /// and the UI both print this and neither should have to invert a negative.
    /// </summary>
    public static SecretShape Secret(SecretRow row) =>
        new(
            row.Id,
            row.Name,
            row.IsPlaceholder ? "empty" : "set",
            row.CreatedAt,
            row.ValueWrittenAt,
            row.DeletedAt);

    public static SecretValueShape Value(SecretValueRow row) =>
        new(Secret(row.Secret), row.Value);

    public static ImportShape Import(ImportSummary summary) =>
        new(
            summary.Created,
            summary.Filled,
            summary.Replaced,
            summary.Unchanged,
            [.. summary.Skipped.Select(one => new SkippedShape(one.Name, one.Reason))],
            [.. summary.Unreadable.Select(one => new UnreadableShape(one.Line, one.Reason))]);
}
