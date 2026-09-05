using Vaultaffe.Infrastructure.Persistence;

namespace Vaultaffe.Api.Hosting;

/// <summary>
/// Runs the expiry sweep, forever, on a timer (Specification §6.5).
/// </summary>
/// <remarks>
/// A window that nothing enforces is a promise rather than a window: without
/// this, a deleted project stays restorable for as long as the instance runs and
/// a superseded credential stays in the database until somebody writes over that
/// secret again. So the sweep is part of the installation and not an operator's
/// cron job — a self-hosted product whose safety property depends on somebody
/// having read the manual does not have that property.
/// <para>
/// <see cref="Interval"/> is the resolution of the deadline rather than the
/// deadline itself: something deleted 72 hours ago is gone within an interval of
/// that, and the interval is the granularity the number is honest to. It runs
/// once at startup too, because an instance that was down over a weekend has a
/// backlog and should not carry it until the first tick.
/// </para>
/// <para>
/// A failed pass is logged and the next one goes ahead. There is nothing to
/// recover: the same rows are still over their deadline, and the next pass finds
/// them.
/// </para>
/// </remarks>
public sealed class ExpiryAtIntervals(
    ExpirySweep sweep, TimeProvider clock, ILogger<ExpiryAtIntervals> logger)
    : BackgroundService
{
    /// <summary>How often the deadline is checked.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var ticks = new PeriodicTimer(Interval, clock);

        do
        {
            try
            {
                await sweep.RunAsync(clock.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "An expiry sweep failed. The next one will try again.");
            }
        }
        while (await Ticked(ticks, stoppingToken));
    }

    private static async Task<bool> Ticked(PeriodicTimer ticks, CancellationToken stoppingToken)
    {
        try
        {
            return await ticks.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
