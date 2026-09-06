using Vaultaffe.Application.Authorization;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.Application.Acts;

/// <summary>One entry of the change log, as a reader sees it.</summary>
/// <remarks>
/// <b>There is no value on this type, and there is no column behind it.</b> Not
/// the new value, not the old one, not a diff (Specification §6.5).
/// </remarks>
public sealed record ChangeRow(
    Guid Id,
    DateTimeOffset OccurredAt,
    ChangeAction Action,
    Guid IdentityId,
    IdentityType IdentityType,
    string IdentityName,
    string? ProjectName,
    string? EnvironmentName,
    string? SecretName);

/// <summary>A page of the log, and how many entries the filter matched.</summary>
public sealed record ChangePage(IReadOnlyList<ChangeRow> Entries, int Total);

/// <summary>
/// A value this secret used to hold — <b>without it</b>. When it was written,
/// when it stopped being current, when it falls out.
/// </summary>
/// <remarks>
/// The listing carries no value on purpose. A history that handed back five old
/// credentials in one answer would be the bulk disclosure this product spends
/// every other decision avoiding, and nothing needs it: a rollback names a
/// version and never opens one.
/// </remarks>
public sealed record VersionRow(
    Guid Id, DateTimeOffset WrittenAt, DateTimeOffset ReplacedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// One identity that has read this secret, and the two moments that are honestly
/// known about it (Specification §6.5).
/// </summary>
/// <remarks>
/// Two moments and no count, because a count would be the beginning of the
/// execution history this deliberately is not — and because nothing that reads a
/// value can tell whether the application it was read for ever started.
/// </remarks>
public sealed record AccessRow(
    Guid IdentityId,
    IdentityType IdentityType,
    string IdentityName,
    DateTimeOffset FirstAt,
    DateTimeOffset LastAt);

/// <summary>
/// Reading the change log (Specification §6.5).
/// </summary>
/// <remarks>
/// <b>A reader has to be able to reach what they are asking about.</b> The
/// entries are by name and carry no ids, so a binding cannot narrow the answer
/// after the fact — instead the caller names the subject, and the same authority
/// as everywhere else says whether they reach it. Asking about the whole
/// organization therefore needs a token that reaches the whole organization; a
/// bound token names its project, and a token bound to one environment names that
/// too.
/// <para>
/// Paging is a limit and an offset over a total order of moment and then id — one
/// act writes several entries at the same instant, and a page boundary in the
/// middle of them has to fall in the same place twice. Entries arriving while a
/// reader pages will shift it, which is what an append-only log does and is not
/// worth a cursor in this product.
/// </para>
/// </remarks>
public sealed class ReadChangeLog(
    IProjectStore projects, IChangeLogStore entries, Authority authority)
{
    /// <summary>How many entries one page holds unless the reader says otherwise.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The most it will hold however loudly the reader asks.</summary>
    public const int MaximumLimit = 200;

    public async Task<ChangePage> ExecuteAsync(
        string? project,
        string? environment,
        string? secret,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 || offset is < 0)
        {
            throw Refusal.Validation(
                limit is < 1 ? "limit" : "offset", "A page is a positive count from a position.");
        }

        var filter = await NarrowedAsync(project, environment, secret, cancellationToken);

        return new ChangePage(
            [
                .. (await entries.ReadAsync(
                    filter,
                    Math.Min(limit ?? DefaultLimit, MaximumLimit),
                    offset ?? 0,
                    cancellationToken)).Select(Row),
            ],
            await entries.CountAsync(filter, cancellationToken));
    }

    /// <summary>
    /// What the caller asked about, once they have been asked whether they reach
    /// it. Naming an environment or a secret without its project is refused
    /// rather than silently widened: a filter that matched every project's
    /// <c>prod</c> is not what anybody meant by it.
    /// </summary>
    private async Task<ChangeLogFilter> NarrowedAsync(
        string? project, string? environment, string? secret, CancellationToken cancellationToken)
    {
        if (project is null)
        {
            if (environment is not null || secret is not null)
            {
                throw Refusal.Validation(
                    "project", "An environment or a secret is named inside a project.");
            }

            authority.RequiresTheWholeOrganization();

            return new ChangeLogFilter();
        }

        var found = await Catalogue.InUseAsync(projects, project, cancellationToken);

        if (environment is null)
        {
            if (secret is not null)
            {
                throw Refusal.Validation(
                    "environment", "A secret is named inside an environment.");
            }

            authority.RequiresAllOf(found.Id);

            return new ChangeLogFilter(found.Name);
        }

        var inside = await Catalogue.InUseAsync(projects, found, environment, cancellationToken);

        authority.RequiresReachInto(found.Id, inside.Id);

        return new ChangeLogFilter(
            found.Name, inside.Name, secret is null ? null : Vault.ANameFrom(secret));
    }

    private static ChangeRow Row(ChangeLogEntry entry) =>
        new(
            entry.Id,
            entry.OccurredAt,
            entry.Action,
            entry.IdentityId,
            entry.IdentityType,
            entry.IdentityName,
            entry.ProjectName,
            entry.EnvironmentName,
            entry.SecretName);
}

/// <summary>
/// What a secret used to hold, newest first — five versions, 72 hours, whichever
/// is reached first (Specification §6.5).
/// </summary>
public sealed class ReadValueHistory(
    IProjectStore projects, ISecretStore secrets, Authority authority)
{
    public async Task<IReadOnlyList<VersionRow>> ExecuteAsync(
        string project, string environment, string name, CancellationToken cancellationToken)
    {
        var (_, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var secret = await Vault.InUseAsync(secrets, inside, name, cancellationToken);

        return
        [
            .. (await secrets.HistoryAsync(secret.Id, cancellationToken))
                .Select(version => new VersionRow(
                    version.Id, version.WrittenAt, version.ReplacedAt, version.ExpiresAt)),
        ];
    }
}

/// <summary>
/// The access summary of one secret: first and last use per identity
/// (Specification §6.5).
/// </summary>
/// <remarks>
/// <b>A summary and not an execution history</b>, and this act is where that has
/// to stay legible: a successful read does not prove that an application started,
/// so what comes back is two moments per identity and nothing that could be
/// mistaken for a run of anything.
/// <para>
/// <c>names</c>, like the change log it sits beside. It says who read a key and
/// when, never what they read.
/// </para>
/// </remarks>
public sealed class ReadAccessSummary(
    IProjectStore projects, ISecretStore secrets, ISecretAccessStore access, Authority authority)
{
    public async Task<IReadOnlyList<AccessRow>> ExecuteAsync(
        string project, string environment, string name, CancellationToken cancellationToken)
    {
        var (_, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var secret = await Vault.InUseAsync(secrets, inside, name, cancellationToken);

        return
        [
            .. (await access.SummaryAsync(secret.Id, cancellationToken))
                .Select(one => new AccessRow(
                    one.IdentityId,
                    one.IdentityType,
                    one.IdentityName,
                    one.FirstAt,
                    one.LastAt)),
        ];
    }
}

/// <summary>
/// Putting back a value this secret used to hold (Specification §6.5).
/// </summary>
/// <remarks>
/// <b>A write like any other, and available to agents.</b> An agent that wrecks a
/// value overnight is exactly who needs an undo button, and denying it one would
/// leave the mess for a person in the morning. It costs `write` and nothing else:
/// there is no <c>replace</c> to give, because asking to undo is already saying
/// what is meant.
/// <para>
/// <b>Nothing is opened.</b> Every version of a secret is sealed under that
/// secret's own data key, so a rollback moves ciphertext and never asks the key
/// ring for anything — the value goes back without passing through this process
/// in the clear.
/// </para>
/// <para>
/// The version rolled back to stops being history, because it is the current
/// value again; what it replaced becomes history in its place. The count is
/// unchanged and the log says what happened.
/// </para>
/// </remarks>
public sealed class RollBackSecret(
    IProjectStore projects,
    ISecretStore secrets,
    ChangeLog log,
    Authority authority,
    TimeProvider clock)
{
    public async Task<SecretRow> ExecuteAsync(
        string project,
        string environment,
        string name,
        Guid? versionId,
        CancellationToken cancellationToken)
    {
        var (found, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var secret = await Vault.InUseAsync(secrets, inside, name, cancellationToken);
        var history = await secrets.HistoryAsync(secret.Id, cancellationToken);

        if (history.Count is 0)
        {
            throw Refusal.NotFound(
                $"'{secret.Name}' has no earlier value to go back to. A value is kept for at "
                + $"most {ValueHistory.Versions} writes or "
                + $"{ValueHistory.Window.TotalHours:0} hours, whichever comes first.");
        }

        // No version named means the one before this: the undo button, pressed
        // once.
        var version = versionId is null
            ? history[0]
            : history.FirstOrDefault(one => one.Id == versionId)
                ?? throw Refusal.NotFound("That is not a version this secret still holds.");

        var now = clock.GetUtcNow();

        // A secret with history holds a value and therefore a data key. Saying so
        // out loud rather than trusting a `!`: the alternative to this line is a
        // 500 on a row nobody can explain.
        var superseded = secret.WrappedDataKey is { } key && !secret.IsPlaceholder
            ? secret.Seal(
                Guid.NewGuid(), new SealedValue(key, version.Nonce, version.Ciphertext), now)!
            : throw Refusal.NotFound(
                $"'{secret.Name}' holds no value to go back from.");

        var kept = history
            .Where(one => one.Id != version.Id)
            .Prepend(superseded)
            .OrderByDescending(one => one.ReplacedAt)
            .Where(one => one.ExpiresAt > now)
            .Take(ValueHistory.Versions)
            .ToHashSet();

        // The version rolled back to leaves the history whatever else happens: it
        // is the current value again, and keeping both would be one credential
        // counted twice against a bound that exists to hold few of them.
        secrets.Keep(
            superseded,
            [.. history.Where(one => one.Id == version.Id || !kept.Contains(one))]);

        log.Record(ChangeAction.ValueRolledBack, found.Name, inside.Name, secret.Name);

        await secrets.SaveAsync(cancellationToken);

        return Vault.Row(secret);
    }
}
