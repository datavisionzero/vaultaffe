using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.Infrastructure.Persistence;

/// <summary>What one pass removed. Counts, because there is nothing else to say.</summary>
public sealed record Expired(int Versions, int Secrets, int Environments, int Projects)
{
    public int Total => Versions + Secrets + Environments + Projects;
}

/// <summary>
/// Scheduled expiry: what the recovery window and the value history promise will
/// eventually be gone, gone (Specification §6.5).
/// </summary>
/// <remarks>
/// Two clocks, and both of them are the domain's. A superseded value goes when
/// <see cref="ValueHistory.Window"/> has passed since it stopped being current; a
/// deleted project, environment or secret goes when
/// <see cref="ValueHistory.RecoveryWindow"/> has passed since it was deleted, and
/// its name frees itself as the row leaves. A purge is the same removal asked for
/// early by a person; this is the deadline arriving on its own.
/// <para>
/// <b>It steps past the organization filter, and it is the only thing here that
/// does for a reason that is not a login.</b> A sweep is the instance acting
/// rather than a caller, so it is inside no organization and the filter would
/// answer it with nothing at all — which is the safe end of that comparison
/// everywhere except here. Every query below says
/// <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/> for
/// exactly that reason.
/// </para>
/// <para>
/// <b>Nothing is written to the change log.</b> An entry names the identity that
/// acted (§6.5) and no identity acted here; a deadline did. The entries about the
/// deletion that started the window stay where they are — neither purge nor
/// expiry removes change-log entries, which is what keeps the log able to say
/// what happened to something that no longer exists.
/// </para>
/// <para>
/// The order is the schema's: there is no cascade between the containers, on
/// purpose, so what hangs underneath is removed first. The one cascade —
/// a secret's versions — is left to the database, which is where it was declared.
/// </para>
/// </remarks>
public sealed class ExpirySweep(IServiceProvider services, ILogger<ExpirySweep> logger)
{
    public async Task<Expired> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<VaultaffeDbContext>();

        var valuesBefore = now - ValueHistory.Window;
        var deletedBefore = now - ValueHistory.RecoveryWindow;

        var versions = await context.SecretValueVersions
            .IgnoreQueryFilters()
            .Where(version => version.ReplacedAt <= valuesBefore)
            .ExecuteDeleteAsync(cancellationToken);

        // Everything under an expiring container goes with it, because there is
        // no cascade between the levels and the promise this enforces is that the
        // subtree was retained as one.
        var environments = context.Environments
            .IgnoreQueryFilters()
            .Where(environment =>
                environment.DeletedAt <= deletedBefore
                || context.Projects
                    .IgnoreQueryFilters()
                    .Any(project =>
                        project.Id == environment.ProjectId
                        && project.DeletedAt <= deletedBefore));

        var secrets = await context.Secrets
            .IgnoreQueryFilters()
            .Where(secret =>
                secret.DeletedAt <= deletedBefore
                || environments.Any(environment => environment.Id == secret.EnvironmentId))
            .ExecuteDeleteAsync(cancellationToken);

        var gone = new Expired(
            versions,
            secrets,
            await environments.ExecuteDeleteAsync(cancellationToken),
            await context.Projects
                .IgnoreQueryFilters()
                .Where(project => project.DeletedAt <= deletedBefore)
                .ExecuteDeleteAsync(cancellationToken));

        if (gone.Total > 0)
        {
            // Counts and never names: what expired is as much a fact about this
            // installation as anything else in a log line, and §6.5 has this one
            // carrying no more than it has to.
            logger.LogInformation(
                "Expiry removed {Versions} value version(s), {Secrets} secret(s), "
                + "{Environments} environment(s) and {Projects} project(s).",
                gone.Versions,
                gone.Secrets,
                gone.Environments,
                gone.Projects);
        }

        return gone;
    }
}
