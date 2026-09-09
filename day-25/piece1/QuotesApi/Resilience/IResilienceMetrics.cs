using Polly.CircuitBreaker;

namespace QuotesApi.Resilience;

public record CircuitTransition(DateTimeOffset At, CircuitState State);

public record ResilienceMetricsSnapshot(
    long DependencyAttempts,
    long DependencySuccesses,
    long DependencyFailures,
    long Retries,
    long Timeouts,
    long BulkheadRejections,
    long CircuitRejections,
    string CircuitState,
    IReadOnlyList<CircuitTransition> Timeline);

/// <summary>
/// Process-wide counters and the circuit's transition history - the "logs/metrics of the breaker
/// opening then half-opening to recovery" this exercise asks for. DependencyAttempts/Successes/
/// Failures increment inside FlakyDependencyHandler itself (the only place a "real" call to the
/// dependency happens), so they're honest regardless of how many times Polly retried internally.
/// Retries/Timeouts/CircuitRejections increment from the resilience pipeline's own OnRetry/
/// OnTimeout callbacks and the enrichment endpoint's exception handling - see
/// InfrastructureExtensions.cs and ResilienceEndpointExtensions.cs.
/// </summary>
public interface IResilienceMetrics
{
    void RecordDependencyAttempt();
    void RecordDependencySuccess();
    void RecordDependencyFailure();
    void RecordRetry();
    void RecordTimeout();
    void RecordBulkheadRejection();
    void RecordCircuitRejection();

    /// <summary>Called from the circuit breaker's OnOpened/OnClosed/OnHalfOpened callbacks -
    /// appends to a bounded timeline rather than just overwriting "current state", so the demo
    /// can show the actual Closed -> Open -> HalfOpen -> Closed sequence, not just a snapshot.</summary>
    void RecordTransition(CircuitState state);

    /// <summary>Wired once at startup to the same CircuitBreakerStateProvider the pipeline uses,
    /// so "what's the state right now" can be answered even between transitions.</summary>
    void AttachStateProvider(CircuitBreakerStateProvider provider);

    ResilienceMetricsSnapshot Snapshot();
    void Reset();
}
