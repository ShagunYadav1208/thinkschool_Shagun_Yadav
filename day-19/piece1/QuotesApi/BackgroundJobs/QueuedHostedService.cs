namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Drains <see cref="IBackgroundTaskQueue"/> one work item at a time. This service is a
/// singleton, so it can't hold a scoped dependency (like IQuoteRepository) directly - each work
/// item gets its own DI scope, created and disposed per iteration.
///
/// Graceful shutdown: on Ctrl+C / SIGTERM, the host cancels <c>stoppingToken</c> and then calls
/// StopAsync, which (via the base BackgroundService implementation) awaits this loop's Task for
/// up to HostOptions.ShutdownTimeout (configured in InfrastructureExtensions) before the process
/// is torn down regardless. Two cases fall out of that:
///   - Waiting on the queue (no job running): DequeueAsync's ReadAsync throws
///     OperationCanceledException immediately, the loop exits, nothing is lost except whatever
///     was never dequeued (the queue itself is in-memory and not persisted).
///   - A job already running: it receives the same stoppingToken, so a job written to check it
///     (like QuoteAnalysisJob) unwinds itself within the shutdown grace window instead of being
///     killed mid-write. A job that ignores the token and runs longer than the grace window is
///     torn down anyway when the timeout expires - the token is cooperative, not a hard kill.
/// </summary>
public class QueuedHostedService(
    IBackgroundTaskQueue taskQueue,
    IServiceScopeFactory scopeFactory,
    ILogger<QueuedHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Queued hosted service starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Func<IServiceProvider, CancellationToken, Task> workItem;

            try
            {
                workItem = await taskQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using var scope = scopeFactory.CreateScope();

            try
            {
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("A queued job was cancelled by application shutdown before it finished.");
            }
            catch (Exception ex)
            {
                // One bad job must not take the drain loop down with it - every other queued
                // job still deserves its turn.
                logger.LogError(ex, "Unhandled exception executing a queued background job.");
            }
        }

        logger.LogInformation("Queued hosted service stopping - drain loop exited.");
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Graceful shutdown requested; giving the in-flight job (if any) time to unwind.");
        return base.StopAsync(cancellationToken);
    }
}
