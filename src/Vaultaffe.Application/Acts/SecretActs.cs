using Vaultaffe.Application.Authorization;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Projects;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Secrets;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Application.Acts;

/// <summary>
/// A secret as the listing shows it: the name, and whether anything is in it.
/// </summary>
/// <remarks>
/// <b>There is no value on this type.</b> That is the shape Specification §6.2
/// asks for and the normal case for an agent — and it is the shape a later MCP
/// server needs, where a tool that returns a value is one that must not exist.
/// </remarks>
public sealed record SecretRow(
    Guid Id,
    string Name,
    bool IsPlaceholder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValueWrittenAt,
    DateTimeOffset? DeletedAt);

/// <summary>
/// One secret and its value — the one answer in this product that carries one,
/// and only because it was asked for by name (§8).
/// </summary>
/// <param name="Value">Null when this is an empty placeholder: there is no value, rather than an empty one.</param>
public sealed record SecretValueRow(SecretRow Secret, string? Value);

/// <summary>What an import did, by name. Never what it wrote.</summary>
/// <param name="Created">Keys that did not exist here before.</param>
/// <param name="Filled">Keys that were empty placeholders and now hold a value.</param>
/// <param name="Replaced">Keys that held a value, written over because the caller asked.</param>
/// <param name="Unchanged">Keys the file had nothing new to say about.</param>
/// <param name="Skipped">Settings not applied, each with a reason that names no value.</param>
/// <param name="Unreadable">Lines the file could not be read as a setting at.</param>
public sealed record ImportSummary(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Filled,
    IReadOnlyList<string> Replaced,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<SkippedSetting> Skipped,
    IReadOnlyList<UnreadableLine> Unreadable);

/// <summary>A setting the import did not apply, and why. The reason names no value.</summary>
public sealed record SkippedSetting(string Name, string Reason);

/// <summary>A line the file could not be read as a setting at, and why.</summary>
public sealed record UnreadableLine(int Line, string Reason);

/// <summary>
/// Finding the environment a secret lives in, and the secret in it.
/// </summary>
internal static class Vault
{
    /// <summary>
    /// The project and the environment named in the path, both in use, with the
    /// caller's reach into that environment already checked.
    /// </summary>
    public static async Task<(Project Project, Environment Environment)> InAsync(
        IProjectStore projects,
        Authority authority,
        string project,
        string environment,
        CancellationToken cancellationToken)
    {
        var found = await Catalogue.InUseAsync(projects, project, cancellationToken);
        var inside = await Catalogue.InUseAsync(projects, found, environment, cancellationToken);

        authority.RequiresReachInto(found.Id, inside.Id);

        return (found, inside);
    }

    public static async Task<Secret> InUseAsync(
        ISecretStore secrets,
        Environment environment,
        string name,
        CancellationToken cancellationToken)
    {
        var found = await secrets.FindAsync(
            environment.Id, ANameFrom(name), cancellationToken);

        return found is null || found.IsDeleted
            ? throw Refusal.NotFound($"No secret called '{name}' in '{environment.Name}'.")
            : found;
    }

    /// <summary>A key off the wire, or the refusal that says what a key looks like.</summary>
    public static string ANameFrom(string? name) =>
        SecretName.IsValid((name ?? string.Empty).Trim())
            ? name!.Trim()
            : throw Refusal.Validation(
                "name",
                $"Use upper-case letters, digits and '_', at most {SecretName.Limit} characters, "
                + "not starting with a digit — a secret becomes an environment variable.");

    public static SecretRow Row(Secret secret) =>
        new(
            secret.Id,
            secret.Name,
            secret.IsPlaceholder,
            secret.CreatedAt,
            secret.ValueWrittenAt,
            secret.DeletedAt);

    /// <summary>
    /// Put a value in place, keep what it replaced, and drop whatever that put
    /// over the bounds (Specification §6.5). The bounds are applied on the write
    /// that creates a version rather than by a sweep, so a history is never over
    /// its limit between one write and some later job.
    /// </summary>
    public static async Task SealAsync(
        ISecretStore secrets,
        IKeyRing keys,
        Secret secret,
        string value,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var history = await secrets.HistoryAsync(secret.Id, cancellationToken);
        var superseded = secret.Seal(
            Guid.NewGuid(), keys.Seal(value, secret.WrappedDataKey), now);

        if (superseded is null)
        {
            return;
        }

        var all = history.Prepend(superseded).OrderByDescending(one => one.ReplacedAt).ToList();

        var kept = all
            .Where(one => one.ExpiresAt > now)
            .Take(ValueHistory.Versions)
            .ToHashSet();

        secrets.Keep(superseded, [.. all.Where(one => !kept.Contains(one))]);
    }
}

/// <summary>
/// Names and status, never values (Specification §6.2, §8).
/// </summary>
/// <remarks>
/// The normal case for an agent, and the reason <c>names</c> is a scope of its
/// own rather than half of "read": an agent that has to know which keys exist,
/// and which of them a human still has to fill, should be able to find out
/// without a token that could read one.
/// </remarks>
public sealed class ListSecrets(
    IProjectStore projects, ISecretStore secrets, Authority authority)
{
    public async Task<IReadOnlyList<SecretRow>> ExecuteAsync(
        string project, string environment, CancellationToken cancellationToken)
    {
        var (_, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        return [.. (await secrets.ListAsync(inside.Id, cancellationToken)).Select(Vault.Row)];
    }
}

/// <summary>
/// One value, asked for by name (Specification §6.2). The one place in this
/// product where a value leaves it other than into a process's environment, and
/// it is deliberately one key at a time.
/// </summary>
public sealed class ReadSecret(
    IProjectStore projects, ISecretStore secrets, IKeyRing keys, Authority authority)
{
    public async Task<SecretValueRow> ExecuteAsync(
        string project, string environment, string name, CancellationToken cancellationToken)
    {
        var (_, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var secret = await Vault.InUseAsync(secrets, inside, name, cancellationToken);

        // A placeholder has no value, rather than an empty one. Answering with
        // an empty string here is exactly the silence the placeholder exists to
        // break (§6.2).
        return new SecretValueRow(
            Vault.Row(secret),
            secret.IsPlaceholder
                ? null
                : keys.Open(new SealedValue(
                    secret.WrappedDataKey!, secret.Nonce!, secret.Ciphertext!)));
    }
}

/// <summary>
/// Writing a value, or creating the empty placeholder that says a human still
/// has to (Specification §6.2, §8 scenario 3).
/// </summary>
/// <remarks>
/// <b>Overwriting is explicit.</b> A key that already holds a value is not
/// written over unless the caller said so — overwriting is as destructive as
/// deleting, and an agent that means to rotate a credential should say that it
/// does. Filling an empty placeholder needs nothing: that is what the placeholder
/// was for.
/// <para>
/// <b>The answer does not carry the value back.</b> A confirmation that echoed it
/// would put into a transcript exactly what piping a vendor's command straight
/// into this act kept out of one.
/// </para>
/// </remarks>
public sealed class SetSecret(
    IProjectStore projects,
    ISecretStore secrets,
    IKeyRing keys,
    ChangeLog log,
    Authority authority,
    TimeProvider clock)
{
    public async Task<SecretRow> ExecuteAsync(
        string project,
        string environment,
        string name,
        string? value,
        bool replace,
        CancellationToken cancellationToken)
    {
        var (found, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var named = Vault.ANameFrom(name);
        var now = clock.GetUtcNow();
        var secret = await secrets.FindAsync(inside.Id, named, cancellationToken);

        if (secret is { IsDeleted: true })
        {
            throw Refusal.NameTaken(
                $"A deleted secret is still called '{named}', and keeps that name for as long as "
                + "it can be restored. Restore it, or purge it, or use another name.",
                bySomethingDeleted: true);
        }

        if (value is not null && !SecretValue.IsValid(value))
        {
            throw Refusal.Validation(
                "value",
                value.Length is 0
                    ? "An empty value is not a value; ask for a placeholder instead."
                    : $"A value is at most {SecretValue.Limit} characters.");
        }

        if (secret is null)
        {
            secret = new Secret(Guid.NewGuid(), found.OrganizationId, inside.Id, named, now);
            secrets.Add(secret);

            log.Record(
                value is null ? ChangeAction.PlaceholderCreated : ChangeAction.Created,
                found.Name,
                inside.Name,
                named);
        }
        else if (value is null)
        {
            // Asking for a placeholder where a value already is, is asking to
            // throw one away by saying nothing. There is no such request.
            throw secret.IsPlaceholder
                ? Refusal.Validation("value", "That key is already an empty placeholder.")
                : Refusal.Validation(
                    "value",
                    $"'{named}' holds a value. Delete it, or write another one over it; a "
                    + "placeholder is not a way of emptying a key.");
        }
        else if (!secret.IsPlaceholder && !replace)
        {
            throw Refusal.ReplaceRequired(named);
        }

        if (value is not null)
        {
            await Vault.SealAsync(secrets, keys, secret, value, now, cancellationToken);
            log.Record(ChangeAction.ValueSet, found.Name, inside.Name, named);
        }

        await secrets.SaveAsync(cancellationToken);

        return Vault.Row(secret);
    }
}

/// <summary>Deleting one, recoverably — an agent may, and it is a scope (§6.4).</summary>
public sealed class DeleteSecret(
    IProjectStore projects,
    ISecretStore secrets,
    ChangeLog log,
    Authority authority,
    TimeProvider clock)
{
    public async Task<SecretRow> ExecuteAsync(
        string project, string environment, string name, CancellationToken cancellationToken)
    {
        var (found, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var secret = await Vault.InUseAsync(secrets, inside, name, cancellationToken);

        secret.DeleteAt(clock.GetUtcNow());
        log.Record(ChangeAction.Deleted, found.Name, inside.Name, secret.Name);

        await secrets.SaveAsync(cancellationToken);

        return Vault.Row(secret);
    }
}

/// <summary>Bringing one back inside its window.</summary>
public sealed class RestoreSecret(
    IProjectStore projects,
    ISecretStore secrets,
    ChangeLog log,
    Authority authority,
    TimeProvider clock)
{
    public async Task<SecretRow> ExecuteAsync(
        string project, string environment, string name, CancellationToken cancellationToken)
    {
        var (found, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var secret = await secrets.FindAsync(
                inside.Id, Vault.ANameFrom(name), cancellationToken)
            ?? throw Refusal.NotFound($"No secret called '{name}' in '{inside.Name}'.");

        if (secret.DeletedAt is { } deletedAt)
        {
            Catalogue.MustStillBeRecoverable(
                "secret", secret.Name, deletedAt, clock.GetUtcNow());

            secret.Restore();
            log.Record(ChangeAction.Restored, found.Name, inside.Name, secret.Name);

            await secrets.SaveAsync(cancellationToken);
        }

        return Vault.Row(secret);
    }
}

/// <summary>
/// A <c>.env</c> file in (Specification §6.2, §11).
/// </summary>
/// <remarks>
/// Migration is a success criterion and must not be something only the web UI can
/// do — the point of taking it through stdin in the CLI is that an agent can
/// migrate a file it never displays. So the whole file arrives here, and what
/// comes back names keys and never values.
/// <para>
/// <c>KEY=</c> is what a file says when a key is there and its value is not, and
/// that is what an empty placeholder means. A line like that creates one rather
/// than being refused for an empty value.
/// </para>
/// <para>
/// One transaction. A file half applied is worse than a file refused, because
/// nobody can tell by looking which half it was.
/// </para>
/// </remarks>
public sealed class ImportSecrets(
    IProjectStore projects,
    ISecretStore secrets,
    IKeyRing keys,
    ChangeLog log,
    Authority authority,
    TimeProvider clock)
{
    public async Task<ImportSummary> ExecuteAsync(
        string project,
        string environment,
        string? content,
        bool replace,
        CancellationToken cancellationToken)
    {
        var (found, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var (settings, complaints) = DotEnv.Read(content);
        var now = clock.GetUtcNow();

        List<string> created = [];
        List<string> filled = [];
        List<string> replaced = [];
        List<string> unchanged = [];
        List<SkippedSetting> skipped = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (var (key, value) in settings)
        {
            // A key twice in one file is a file somebody edited twice. Which of
            // the two lines was meant is not knowable, and quietly taking one is
            // how an import writes a credential nobody chose.
            if (!seen.Add(key))
            {
                skipped.Add(new SkippedSetting(key, "That key is set more than once in this file."));
                continue;
            }

            if (value.Length > SecretValue.Limit)
            {
                skipped.Add(new SkippedSetting(key, "Over the length a value may be."));
                continue;
            }

            var secret = await secrets.FindAsync(inside.Id, key, cancellationToken);

            if (secret is { IsDeleted: true })
            {
                skipped.Add(new SkippedSetting(
                    key, "A deleted secret still holds that name until it is restored or purged."));
                continue;
            }

            if (secret is null)
            {
                secret = new Secret(Guid.NewGuid(), found.OrganizationId, inside.Id, key, now);
                secrets.Add(secret);

                log.Record(
                    value.Length is 0 ? ChangeAction.PlaceholderCreated : ChangeAction.Created,
                    found.Name,
                    inside.Name,
                    key);

                created.Add(key);
            }
            else if (value.Length is 0)
            {
                // The file says nothing about this key's value, and a key that
                // already exists is not emptied by a file that is silent.
                unchanged.Add(key);
                continue;
            }
            else if (secret.IsPlaceholder)
            {
                filled.Add(key);
            }
            else if (!replace)
            {
                skipped.Add(new SkippedSetting(
                    key, "It already holds a value, and overwriting one is explicit."));
                continue;
            }
            else
            {
                replaced.Add(key);
            }

            if (value.Length > 0)
            {
                await Vault.SealAsync(secrets, keys, secret, value, now, cancellationToken);
                log.Record(ChangeAction.ValueSet, found.Name, inside.Name, key);
            }
        }

        await secrets.SaveAsync(cancellationToken);

        return new ImportSummary(
            created,
            filled,
            replaced,
            unchanged,
            skipped,
            [.. complaints.Select(one => new UnreadableLine(one.Line, one.Reason))]);
    }
}

/// <summary>
/// Every value of an environment, in plaintext (Specification §6.2).
/// </summary>
/// <remarks>
/// This is precisely the contradiction <c>inject</c> was rejected for (§7), and
/// it stays anyway: a product that holds your credentials and offers no way back
/// out is asking for a trust it has not earned. What makes it defensible is the
/// one restriction on it — it needs a person. The endpoint declares
/// <c>HumanAction.Export</c>, and an agent or a service token is refused with the
/// action named rather than with a wall.
/// <para>
/// Nothing is recorded: the change log holds mutations rather than reads (§6.5),
/// and an export changes nothing. An access log is a different feature and the
/// MVP does not pretend to one.
/// </para>
/// </remarks>
public sealed class ExportEnvironment(
    IProjectStore projects, ISecretStore secrets, IKeyRing keys, Authority authority)
{
    public async Task<string> ExecuteAsync(
        string project, string environment, CancellationToken cancellationToken)
    {
        var (_, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var all = await secrets.ListAsync(inside.Id, cancellationToken);

        return DotEnv.Write(all.Select(secret => new DotEnv.Setting(
            secret.Name,
            secret.IsPlaceholder
                ? string.Empty
                : keys.Open(new SealedValue(
                    secret.WrappedDataKey!, secret.Nonce!, secret.Ciphertext!)))));
    }
}
