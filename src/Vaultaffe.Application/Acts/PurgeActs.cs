using Vaultaffe.Application.Authorization;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.History;
using Vaultaffe.Domain.Refusals;

namespace Vaultaffe.Application.Acts;

/// <summary>
/// What a purge removed, by name. Never a value, and never a count of them
/// standing in for one.
/// </summary>
public sealed record Purged(
    string? Project, string? Environment, string? Secret, int Versions);

/// <summary>
/// Removing a deleted project and everything retained under it, now rather than
/// when its window ends (Specification §6.5).
/// </summary>
/// <remarks>
/// <b>Human-only, and the reason is not that it is dangerous.</b> Everything an
/// agent may do here is recoverable, and this is the one action that destroys the
/// undo button — in an agent's hands it would be anti-forensics. So it is on the
/// short list of §6.4 and the endpoint declares it.
/// <para>
/// <b>Only something already deleted can be purged.</b> A purge is not a faster
/// delete; it is the second half of one, and asking for it on something in use
/// would be asking to skip the window that makes deletion safe.
/// </para>
/// <para>
/// The change-log entries stay. Neither purge nor expiry removes them, which is
/// what keeps the log able to say what happened to something that no longer
/// exists — and it is why the log records names rather than keys
/// (<c>docs/storage.md</c>).
/// </para>
/// </remarks>
public sealed class PurgeProject(IProjectStore projects, ChangeLog log, Authority authority)
{
    public async Task<Purged> ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        var project = await projects.FindProjectAsync(
                Catalogue.ANameFrom(name, "project"), cancellationToken)
            ?? throw Refusal.NotFound($"No project called '{name}'.");

        authority.RequiresAllOf(project.Id);

        if (!project.IsDeleted)
        {
            throw Refusal.Validation(
                "project",
                $"'{project.Name}' is in use. Delete it first: a purge removes what a deletion "
                + "retained, it is not a faster way of deleting.");
        }

        log.Record(ChangeAction.Purged, project: project.Name);

        await projects.PurgeAsync(project, cancellationToken);
        await projects.SaveAsync(cancellationToken);

        return new Purged(project.Name, null, null, 0);
    }
}

/// <summary>The same, one level down: a deleted environment and the secrets in it.</summary>
public sealed class PurgeEnvironment(IProjectStore projects, ChangeLog log, Authority authority)
{
    public async Task<Purged> ExecuteAsync(
        string project, string name, CancellationToken cancellationToken)
    {
        var found = await Catalogue.InUseAsync(projects, project, cancellationToken);

        var environment = await projects.FindEnvironmentAsync(
                found.Id, Catalogue.ANameFrom(name, "environment"), cancellationToken)
            ?? throw Refusal.NotFound($"No environment called '{name}' in '{found.Name}'.");

        authority.RequiresAllOf(found.Id);

        if (!environment.IsDeleted)
        {
            throw Refusal.Validation(
                "environment",
                $"'{environment.Name}' is in use. Delete it first: a purge removes what a "
                + "deletion retained, it is not a faster way of deleting.");
        }

        log.Record(ChangeAction.Purged, found.Name, environment.Name);

        await projects.PurgeAsync(environment, cancellationToken);
        await projects.SaveAsync(cancellationToken);

        return new Purged(found.Name, environment.Name, null, 0);
    }
}

/// <summary>A deleted secret and everything it ever held.</summary>
public sealed class PurgeSecret(
    IProjectStore projects, ISecretStore secrets, ChangeLog log, Authority authority)
{
    public async Task<Purged> ExecuteAsync(
        string project, string environment, string name, CancellationToken cancellationToken)
    {
        var (found, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var secret = await secrets.FindAsync(
                inside.Id, Vault.ANameFrom(name), cancellationToken)
            ?? throw Refusal.NotFound($"No secret called '{name}' in '{inside.Name}'.");

        if (!secret.IsDeleted)
        {
            throw Refusal.Validation(
                "secret",
                $"'{secret.Name}' is in use. Delete it first, or purge its history instead — a "
                + "purge removes what a deletion retained.");
        }

        var versions = (await secrets.HistoryAsync(secret.Id, cancellationToken)).Count;

        log.Record(ChangeAction.Purged, found.Name, inside.Name, secret.Name);

        secrets.Purge(secret);

        await secrets.SaveAsync(cancellationToken);

        return new Purged(found.Name, inside.Name, secret.Name, versions);
    }
}

/// <summary>
/// Removing what a secret used to hold, keeping the secret and its current value.
/// </summary>
/// <remarks>
/// The headline case of Specification §6.5: after a suspected compromise the
/// normal expectation is that a key's history genuinely disappears, and neither
/// Doppler nor AWS offers that cleanly. Human-only like every other purge — it is
/// the same undo button, removed for the same reason.
/// </remarks>
public sealed class PurgeValueHistory(
    IProjectStore projects, ISecretStore secrets, ChangeLog log, Authority authority)
{
    public async Task<Purged> ExecuteAsync(
        string project, string environment, string name, CancellationToken cancellationToken)
    {
        var (found, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var secret = await Vault.InUseAsync(secrets, inside, name, cancellationToken);
        var versions = (await secrets.HistoryAsync(secret.Id, cancellationToken)).Count;

        log.Record(ChangeAction.Purged, found.Name, inside.Name, secret.Name);

        await secrets.PurgeHistoryAsync(secret.Id, cancellationToken);
        await secrets.SaveAsync(cancellationToken);

        return new Purged(found.Name, inside.Name, secret.Name, versions);
    }
}
