namespace QuotesApi.BackgroundJobs;

public interface IBackgroundTaskQueue
{
    ValueTask QueueAsync(
        Func<IServiceProvider, CancellationToken, Task> workItem,
        CancellationToken cancellationToken = default);

    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(
        CancellationToken cancellationToken);
}
