using System.Threading.Channels;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Channel-backed queue between request threads (producers, via POST /api/jobs/...) and
/// QueuedHostedService (the single consumer). Bounded so a burst of enqueues applies
/// backpressure - QueueAsync's WriteAsync awaits instead of growing memory without limit - rather
/// than an unbounded channel silently accepting everything.
/// </summary>
public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel;

    public BackgroundTaskQueue(int capacity = 100)
    {
        _channel = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait });
    }

    public async ValueTask QueueAsync(
        Func<IServiceProvider, CancellationToken, Task> workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        await _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    public async ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(
        CancellationToken cancellationToken)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }
}
