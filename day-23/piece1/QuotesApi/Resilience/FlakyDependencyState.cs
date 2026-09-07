using System.Text.Json.Serialization;

namespace QuotesApi.Resilience;

[JsonConverter(typeof(JsonStringEnumConverter<FlakyDependencyMode>))]
public enum FlakyDependencyMode
{
    /// <summary>Every call succeeds after LatencyMs.</summary>
    Healthy,

    /// <summary>Every call fails (503) after LatencyMs - drives the circuit breaker demo.</summary>
    AlwaysFail,

    /// <summary>Every call succeeds, but LatencyMs is meant to be set above the pipeline's
    /// per-attempt timeout - drives the timeout demo.</summary>
    Slow,

    /// <summary>Each call independently fails with probability FailureRatePercent.</summary>
    Intermittent,
}

public record FlakyDependencySnapshot(FlakyDependencyMode Mode, int LatencyMs, int FailureRatePercent);

/// <summary>
/// The "outbound dependency" this exercise wraps with Polly. A real third-party API would work
/// identically from the resilience pipeline's point of view - the pipeline only ever sees a
/// Task&lt;HttpResponseMessage&gt; that succeeds, fails, or takes too long. Modeling it in-process
/// (see FlakyDependencyHandler) means the failure/latency behavior is fully deterministic and
/// controllable from the demo UI, instead of depending on some real external service's actual
/// uptime to prove the breaker opens and recovers.
/// </summary>
public sealed class FlakyDependencyState
{
    private readonly Lock _gate = new();
    private FlakyDependencyMode _mode = FlakyDependencyMode.Healthy;
    private int _latencyMs = 30;
    private int _failureRatePercent = 100;

    public FlakyDependencySnapshot Snapshot()
    {
        lock (_gate)
            return new FlakyDependencySnapshot(_mode, _latencyMs, _failureRatePercent);
    }

    public void Configure(FlakyDependencyMode mode, int? latencyMs, int? failureRatePercent)
    {
        lock (_gate)
        {
            _mode = mode;

            if (latencyMs is > 0)
                _latencyMs = latencyMs.Value;

            if (failureRatePercent is >= 0 and <= 100)
                _failureRatePercent = failureRatePercent.Value;
        }
    }

    /// <summary>Decides this one call's outcome. Called once per attempt from inside
    /// FlakyDependencyHandler - every retry re-evaluates it, same as a real flaky dependency
    /// would produce a fresh (possibly different) outcome on each attempt.</summary>
    public (bool ShouldFail, int DelayMs) NextOutcome()
    {
        lock (_gate)
        {
            var shouldFail = _mode switch
            {
                FlakyDependencyMode.Healthy => false,
                FlakyDependencyMode.AlwaysFail => true,
                FlakyDependencyMode.Slow => false,
                FlakyDependencyMode.Intermittent => Random.Shared.Next(100) < _failureRatePercent,
                _ => false,
            };

            return (shouldFail, _latencyMs);
        }
    }
}
