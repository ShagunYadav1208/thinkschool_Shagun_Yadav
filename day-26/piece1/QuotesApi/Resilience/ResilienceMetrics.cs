using Polly.CircuitBreaker;

namespace QuotesApi.Resilience;

public sealed class ResilienceMetrics : IResilienceMetrics
{
    private const int MaxTimelineEntries = 25;

    private long _dependencyAttempts;
    private long _dependencySuccesses;
    private long _dependencyFailures;
    private long _retries;
    private long _timeouts;
    private long _bulkheadRejections;
    private long _circuitRejections;

    private readonly Lock _timelineGate = new();
    private readonly List<CircuitTransition> _timeline = [];

    private CircuitBreakerStateProvider? _stateProvider;

    public void RecordDependencyAttempt() => Interlocked.Increment(ref _dependencyAttempts);
    public void RecordDependencySuccess() => Interlocked.Increment(ref _dependencySuccesses);
    public void RecordDependencyFailure() => Interlocked.Increment(ref _dependencyFailures);
    public void RecordRetry() => Interlocked.Increment(ref _retries);
    public void RecordTimeout() => Interlocked.Increment(ref _timeouts);
    public void RecordBulkheadRejection() => Interlocked.Increment(ref _bulkheadRejections);
    public void RecordCircuitRejection() => Interlocked.Increment(ref _circuitRejections);

    public void RecordTransition(CircuitState state)
    {
        lock (_timelineGate)
        {
            _timeline.Add(new CircuitTransition(DateTimeOffset.UtcNow, state));

            if (_timeline.Count > MaxTimelineEntries)
                _timeline.RemoveAt(0);
        }
    }

    public void AttachStateProvider(CircuitBreakerStateProvider provider) => _stateProvider = provider;

    public ResilienceMetricsSnapshot Snapshot()
    {
        lock (_timelineGate)
        {
            return new ResilienceMetricsSnapshot(
                Interlocked.Read(ref _dependencyAttempts),
                Interlocked.Read(ref _dependencySuccesses),
                Interlocked.Read(ref _dependencyFailures),
                Interlocked.Read(ref _retries),
                Interlocked.Read(ref _timeouts),
                Interlocked.Read(ref _bulkheadRejections),
                Interlocked.Read(ref _circuitRejections),
                _stateProvider?.CircuitState.ToString() ?? CircuitState.Closed.ToString(),
                [.. _timeline]);
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _dependencyAttempts, 0);
        Interlocked.Exchange(ref _dependencySuccesses, 0);
        Interlocked.Exchange(ref _dependencyFailures, 0);
        Interlocked.Exchange(ref _retries, 0);
        Interlocked.Exchange(ref _timeouts, 0);
        Interlocked.Exchange(ref _bulkheadRejections, 0);
        Interlocked.Exchange(ref _circuitRejections, 0);

        lock (_timelineGate)
            _timeline.Clear();
    }
}
