using Microsoft.Extensions.DependencyInjection;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Identities;

namespace Vaultaffe.Api.Hosting;

/// <summary>
/// Makes this instance's claim secret if it has none, and prints it for as long
/// as nobody has claimed the instance (ADR 0019).
/// </summary>
/// <remarks>
/// <para>
/// <b>Before anything is served</b>, and after the migrations: a hosted service
/// starts before the server accepts a request, so there is no window in which
/// the first run is reachable and the secret it needs does not exist yet. It is
/// registered after <see cref="SchemaAtStartup"/>, which is what puts the table
/// there to write into.
/// </para>
/// <para>
/// <b>At every start, not only the one that made it.</b> An operator who closed
/// the terminal, or who came to the machine a week later, restarts the container
/// and reads the same secret again. That is what makes this safe to adopt:
/// nothing about it is unrecoverable.
/// </para>
/// <para>
/// <b>This is the one value this product prints, and it is named as an
/// exception.</b> Specification §6.5 makes a value in a log line a bug rather
/// than an untidiness, and that rule is not softened here: this is not a secret
/// of anybody's, it is the key to an empty instance, and it stops existing the
/// moment the instance holds anything worth keeping.
/// </para>
/// </remarks>
public sealed class ClaimSecretAtStartup(
    IServiceProvider services,
    TimeProvider clock,
    ILogger<ClaimSecretAtStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        var identities = scope.ServiceProvider.GetRequiredService<IIdentityStore>();

        if (await identities.FindTheOrganizationAsync(cancellationToken) is not null)
        {
            // Claimed. There is nothing to print and nothing to make: the first
            // run took the row with it.
            return;
        }

        var claim = await identities.FindTheClaimAsync(cancellationToken);

        if (claim is null)
        {
            claim = InstanceClaim.Issue(Guid.NewGuid(), clock.GetUtcNow());

            await identities.AddClaimAsync(claim, cancellationToken);
        }

        // Deliberately one block and deliberately loud. An operator scrolling a
        // `docker compose logs` has to be able to find this without knowing what
        // they are looking for, and the sentence has to say what to do with it —
        // a random string with no instruction beside it is a support question.
        logger.LogWarning(
            "This instance has not been claimed yet. Whoever completes the first run becomes "
            + "its administrator, and the first run needs the claim secret below.\n"
            + "\n"
            + "    {ClaimSecret}\n"
            + "\n"
            + "Paste it into the first-run page, or pass it to `vaultaffe instance start`. It "
            + "is printed at every start until somebody claims this instance, and it stops "
            + "working the moment they do.",
            claim.Secret);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
