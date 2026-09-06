using Vaultaffe.Application.Acts;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Api.Http;

/// <summary>Which earlier value to put back. Omitted means the one before this.</summary>
public sealed record RollBackRequest(Guid? VersionId = null);

/// <summary>One entry of the change log. There is no value on it, and no column behind one.</summary>
public sealed record ChangeShape(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    IdentityShape Identity,
    string? Project,
    string? Environment,
    string? Secret);

/// <summary>Who acted, and what kind of thing they were.</summary>
public sealed record IdentityShape(Guid Id, string Type, string Name);

/// <summary>A page of the log, and how many entries the filter matched.</summary>
public sealed record ChangePageShape(IReadOnlyList<ChangeShape> Entries, int Total);

/// <summary>
/// One identity that has read this secret, and when it first and last did.
/// </summary>
/// <remarks>
/// Two moments and no count. A summary rather than an execution history
/// (Specification §6.5): a successful read does not prove that an application
/// started, and a number here would read as though it did.
/// </remarks>
public sealed record AccessShape(
    IdentityShape Identity, DateTimeOffset FirstAt, DateTimeOffset LastAt);

/// <summary>A value a secret used to hold — when, until when, and until when it is kept.</summary>
public sealed record VersionShape(
    Guid Id, DateTimeOffset WrittenAt, DateTimeOffset ReplacedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// The change log and the value history (<c>docs/api.md</c>) — two things with
/// completely different risk profiles, and therefore two endpoints that share
/// nothing (Specification §6.5).
/// </summary>
/// <remarks>
/// The log records mutations and holds no value. The history holds values under
/// tight bounds and hands none of them out: it says when, and a rollback names a
/// version rather than reading one.
/// <para>
/// Reads are not in the log, and that is a decision rather than a gap: <c>run</c>
/// reads values, but a successful read does not prove an application started, and
/// the MVP does not pretend to an execution history.
/// </para>
/// </remarks>
public static class HistoryEndpoints
{
    private const string Tag = "History";

    public static IEndpointRouteBuilder MapHistory(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet($"{ApiVersion.Route}/changes", async (
                string? project,
                string? environment,
                string? secret,
                int? limit,
                int? offset,
                ReadChangeLog read,
                CancellationToken cancellation) =>
                Recorded.Page(await read.ExecuteAsync(
                    project, environment, secret, limit, offset, cancellation)))
            .WithTags(Tag)
            .Needing(Scopes.Names)
            .WithName("ReadChanges")
            .WithSummary("What was changed, by whom, and of what kind. Never a value.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var api = endpoints
            .MapGroup($"{ApiVersion.Route}/projects/{{project}}/environments/{{environment}}/secrets")
            .WithTags(Tag);

        api.MapGet("/{name}/versions", async (
                string project,
                string environment,
                string name,
                ReadValueHistory read,
                CancellationToken cancellation) =>
                (await read.ExecuteAsync(project, environment, name, cancellation))
                    .Select(Recorded.Version).ToList())
            .Needing(Scopes.Names)
            .WithName("ReadSecretVersions")
            .WithSummary("What this secret used to hold: when, and until when it is kept.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/{name}/access", async (
                string project,
                string environment,
                string name,
                ReadAccessSummary read,
                CancellationToken cancellation) =>
                (await read.ExecuteAsync(project, environment, name, cancellation))
                    .Select(Recorded.Access).ToList())
            .Needing(Scopes.Names)
            .WithName("ReadSecretAccess")
            .WithSummary("Who has read this key, and when they first and last did. Never what.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/{name}/rollback", async (
                string project,
                string environment,
                string name,
                RollBackRequest? request,
                RollBackSecret rollBack,
                CancellationToken cancellation) =>
                Results.Ok(Vaulted.Secret(await rollBack.ExecuteAsync(
                    project, environment, name, request?.VersionId, cancellation))))
            .Needing(Scopes.Write)
            .WithName("RollBackSecret")
            .WithSummary("Put an earlier value back. A write like any other, and agents may.")
            .Produces<SecretShape>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

/// <summary>What was recorded, as the contract shows it.</summary>
public static class Recorded
{
    /// <summary>The wire spelling of an action: <c>value-rolled-back</c>.</summary>
    public static string ActionOf(ChangeAction action) => action switch
    {
        ChangeAction.Created => "created",
        ChangeAction.Renamed => "renamed",
        ChangeAction.ValueSet => "value-set",
        ChangeAction.ValueRolledBack => "value-rolled-back",
        ChangeAction.PlaceholderCreated => "placeholder-created",
        ChangeAction.Deleted => "deleted",
        ChangeAction.Restored => "restored",
        ChangeAction.Purged => "purged",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "An action with no name."),
    };

    /// <summary>
    /// The wire spelling of an identity type. This is the field Specification
    /// §6.5 exists for: with writing agents, what kind of thing acted is the
    /// interesting question.
    /// </summary>
    public static string TypeOf(IdentityType type) => type switch
    {
        IdentityType.HumanSession => "human-session",
        IdentityType.ServiceToken => "service-token",
        IdentityType.AgentToken => "agent-token",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "An identity with no type."),
    };

    public static ChangeShape Change(ChangeRow row) =>
        new(
            row.Id,
            row.OccurredAt,
            ActionOf(row.Action),
            new IdentityShape(row.IdentityId, TypeOf(row.IdentityType), row.IdentityName),
            row.ProjectName,
            row.EnvironmentName,
            row.SecretName);

    public static ChangePageShape Page(ChangePage page) =>
        new([.. page.Entries.Select(Change)], page.Total);

    public static VersionShape Version(VersionRow row) =>
        new(row.Id, row.WrittenAt, row.ReplacedAt, row.ExpiresAt);

    public static AccessShape Access(AccessRow row) =>
        new(
            new IdentityShape(row.IdentityId, TypeOf(row.IdentityType), row.IdentityName),
            row.FirstAt,
            row.LastAt);
}
