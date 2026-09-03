// Matches thinkschool_Shagun_Yadav/day-21/piece1/QuotesApi/Caching/ICacheMetrics.cs's
// CacheMetricsSnapshot record - the shape GET /api/cache/metrics actually returns.
export interface CacheMetrics {
  dbReads: number;
  cacheRequests: number;
  cacheMisses: number;
  cacheHits: number;
  hitRatePercent: number;
}
