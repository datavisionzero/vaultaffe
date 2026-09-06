using Vaultaffe.Application.Authorization;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Secrets;
using Environment = Vaultaffe.Domain.Environments.Environment;

namespace Vaultaffe.Application.Acts;

/// <summary>
/// One line of the notice: a key this environment has not got, the environments
/// that have it, and when somebody told the notice to stop saying so.
/// </summary>
public sealed record MissingKeyRow(
    string Name, IReadOnlyList<string> PresentIn, DateTimeOffset? DismissedAt);

/// <summary>
/// Finding the environments a notice is computed over, which is the only part of
/// this that is not arithmetic.
/// </summary>
internal static class Neighbours
{
    /// <summary>
    /// The keys of every environment of that project the caller may see, by
    /// environment name — the target included.
    /// </summary>
    /// <remarks>
    /// <b>Only the environments the caller reaches.</b> A token bound to
    /// <c>prod</c> must not learn from a notice which keys exist in <c>dev</c>:
    /// the binding is what a token may touch (§6.4), and a comparison is a way of
    /// touching. A token that reaches one environment therefore gets an empty
    /// notice rather than a filtered one, because there is nothing to compare it
    /// with — which is the honest answer and not a degraded feature.
    /// </remarks>
    public static async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> KeysAsync(
        IProjectStore projects,
        ISecretStore secrets,
        Authority authority,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var reachable = (await projects.ListEnvironmentsAsync(projectId, cancellationToken))
            .Where(environment => authority.Caller.Reaches(projectId, environment.Id))
            .ToList();

        var byId = await Grouped(secrets, reachable, cancellationToken);

        return reachable.ToDictionary(
            environment => environment.Name,
            environment => byId.TryGetValue(environment.Id, out var keys)
                ? keys
                : (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static async Task<Dictionary<Guid, IReadOnlySet<string>>> Grouped(
        ISecretStore secrets,
        IReadOnlyList<Environment> environments,
        CancellationToken cancellationToken)
    {
        var rows = await secrets.ListAsync(
            [.. environments.Select(environment => environment.Id)], cancellationToken);

        return rows
            .GroupBy(secret => secret.EnvironmentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<string>)group
                    .Select(secret => secret.Name)
                    .ToHashSet(StringComparer.Ordinal));
    }
}

/// <summary>
/// The missing-key notice for one environment (Specification §6.1).
/// </summary>
/// <remarks>
/// <c>names</c> and nothing more, because that is all this ever says: which keys
/// exist where. No value is read, and none could be — the rule
/// (<see cref="MissingKeys"/>) is arithmetic over names.
/// <para>
/// <b>Dismissed lines come back with the rest</b>, carrying the moment somebody
/// silenced them, rather than behind a second question the way deleted rows do.
/// A notice is one small thing a reader looks at once, and the screen that offers
/// to take a dismissal back needs both halves of it to say how many there are —
/// two requests for that would be two requests for every visit to an environment.
/// </para>
/// </remarks>
public sealed class ReadMissingKeys(
    IProjectStore projects, ISecretStore secrets, Authority authority)
{
    public async Task<IReadOnlyList<MissingKeyRow>> ExecuteAsync(
        string project, string environment, CancellationToken cancellationToken)
    {
        var (found, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var reported = await Notice.OfAsync(
            projects, secrets, authority, found.Id, inside, cancellationToken);

        var silenced = (await secrets.DismissalsAsync(inside.Id, cancellationToken))
            .ToDictionary(one => one.Name, one => one.DismissedAt, StringComparer.Ordinal);

        return
        [
            .. reported.Select(missing => new MissingKeyRow(
                missing.Name,
                missing.PresentIn,
                silenced.TryGetValue(missing.Name, out var at) ? at : null)),
        ];
    }
}

/// <summary>
/// Stop the notice mentioning that key in that environment (§6.1).
/// </summary>
/// <remarks>
/// <b>Dismissing writes nothing to the vault</b>, which is why it records no
/// change-log entry: the log is what changed about a secret (§6.5), and after
/// this nothing about one has. It needs <c>write</c> all the same, because it is
/// a change to what everybody in the organization sees.
/// <para>
/// Dismissing twice is dismissing once. Two people looking at the same notice is
/// the normal case, and the second of them should not be told the key does not
/// exist.
/// </para>
/// </remarks>
public sealed class DismissMissingKey(
    IProjectStore projects, ISecretStore secrets, Authority authority, TimeProvider clock)
{
    public async Task<MissingKeyRow> ExecuteAsync(
        string project, string environment, string name, CancellationToken cancellationToken)
    {
        var (found, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var key = Vault.ANameFrom(name);
        var missing = await Notice.OneAsync(
            projects, secrets, authority, found.Id, inside, key, cancellationToken);

        var already = (await secrets.DismissalsAsync(inside.Id, cancellationToken))
            .FirstOrDefault(one => one.Name == key);

        if (already is not null)
        {
            return new MissingKeyRow(missing.Name, missing.PresentIn, already.DismissedAt);
        }

        var moment = clock.GetUtcNow();

        secrets.Add(new DismissedKey(
            Guid.NewGuid(), authority.Caller.OrganizationId, inside.Id, key, moment));

        await secrets.SaveAsync(cancellationToken);

        return new MissingKeyRow(missing.Name, missing.PresentIn, moment);
    }
}

/// <summary>
/// Take a dismissal back, so the notice mentions that key here again.
/// </summary>
public sealed class WithdrawDismissal(
    IProjectStore projects, ISecretStore secrets, Authority authority)
{
    public async Task<MissingKeyRow> ExecuteAsync(
        string project, string environment, string name, CancellationToken cancellationToken)
    {
        var (found, inside) = await Vault.InAsync(
            projects, authority, project, environment, cancellationToken);

        var key = Vault.ANameFrom(name);
        var dismissal = (await secrets.DismissalsAsync(inside.Id, cancellationToken))
            .FirstOrDefault(one => one.Name == key)
            ?? throw Refusal.NotFound(
                $"'{key}' is not dismissed in '{inside.Name}'.");

        secrets.Remove(dismissal);
        await secrets.SaveAsync(cancellationToken);

        var reported = await Notice.OfAsync(
            projects, secrets, authority, found.Id, inside, cancellationToken);

        // The key may not be missing any more — somebody may have created it
        // while the dismissal stood — and then withdrawing leaves nothing to say
        // about it, which is exactly right and not an error.
        var missing = reported.FirstOrDefault(one => one.Name == key);

        return new MissingKeyRow(key, missing?.PresentIn ?? [], null);
    }
}

/// <summary>
/// The notice itself, before dismissals are applied to it — which is the list
/// both acts above have to agree on.
/// </summary>
internal static class Notice
{
    public static async Task<IReadOnlyList<MissingKey>> OfAsync(
        IProjectStore projects,
        ISecretStore secrets,
        Authority authority,
        Guid projectId,
        Environment environment,
        CancellationToken cancellationToken) =>
        MissingKeys.In(
            environment.Name,
            await Neighbours.KeysAsync(projects, secrets, authority, projectId, cancellationToken));

    /// <summary>
    /// One line of it, or the refusal that says there is nothing to dismiss. The
    /// list is the undismissed-and-dismissed one on purpose: a key somebody
    /// already silenced is still a key the notice is about, and dismissing it a
    /// second time has to be a no-op rather than a 404.
    /// </summary>
    public static async Task<MissingKey> OneAsync(
        IProjectStore projects,
        ISecretStore secrets,
        Authority authority,
        Guid projectId,
        Environment environment,
        string name,
        CancellationToken cancellationToken) =>
        (await OfAsync(projects, secrets, authority, projectId, environment, cancellationToken))
            .FirstOrDefault(one => one.Name == name)
        ?? throw Refusal.NotFound(
            $"The notice for '{environment.Name}' does not mention '{name}': it is not a key that "
            + "most of this project's other environments have.");
}
