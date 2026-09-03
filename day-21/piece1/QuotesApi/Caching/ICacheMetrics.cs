namespace QuotesApi.Caching;

/// <summary>
/// Process-wide counters used to prove the cache is doing something, not just wired up.
/// DbReads increments inside QuoteRepository itself (the only place a real DB round trip
/// happens), so it's an honest count regardless of whether the caller went through
/// HybridCache or the uncached comparison endpoint. CacheRequests/CacheMisses are recorded
/// only by the cached GET /api/quotes/{id} path, so (requests - misses) / requests is the
/// cache's real hit rate for that endpoint.
/// </summary>
public interface ICacheMetrics
{
    long DbReads { get; }
    long CacheRequests { get; }
    long CacheMisses { get; }

    void RecordDbRead();
    void RecordCacheRequest();
    void RecordCacheMiss();
    void Reset();
}

public record CacheMetricsSnapshot(long DbReads, long CacheRequests, long CacheMisses, long CacheHits, double HitRatePercent);
