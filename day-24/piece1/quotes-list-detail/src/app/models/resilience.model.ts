// Matches thinkschool_Shagun_Yadav/day-22/piece1/QuotesApi/Resilience/IResilienceMetrics.cs's
// ResilienceMetricsSnapshot record - the shape GET /api/resilience/metrics returns.

export type CircuitState = 'Closed' | 'Open' | 'HalfOpen' | 'Isolated';

export interface CircuitTransition {
  at: string;
  state: number;
}

export interface ResilienceMetrics {
  dependencyAttempts: number;
  dependencySuccesses: number;
  dependencyFailures: number;
  retries: number;
  timeouts: number;
  bulkheadRejections: number;
  circuitRejections: number;
  circuitState: CircuitState;
  timeline: CircuitTransition[];
}

export type FlakyDependencyMode = 'Healthy' | 'AlwaysFail' | 'Slow' | 'Intermittent';

export interface FlakyDependencySnapshot {
  mode: FlakyDependencyMode;
  latencyMs: number;
  failureRatePercent: number;
}

export interface EnrichResult {
  outcome: 'success' | 'circuit-open' | 'bulkhead-rejected' | 'timeout' | 'dependency-failed';
  enrichment: { quoteId: number; enrichment: string } | null;
  detail: string | null;
}
