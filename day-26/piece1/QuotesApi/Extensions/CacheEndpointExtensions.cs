using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Caching;

namespace QuotesApi.Extensions;

public static class CacheEndpointExtensions
{
    public static void MapCacheEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/cache");

        group.MapGet("/metrics", (ICacheMetrics metrics) =>
        {
            var requests = metrics.CacheRequests;
            var misses = metrics.CacheMisses;
            var hits = requests - misses;
            var hitRatePercent = requests == 0 ? 0 : Math.Round(100.0 * hits / requests, 2);

            return Results.Ok(new CacheMetricsSnapshot(metrics.DbReads, requests, misses, hits, hitRatePercent));
        });

        // Zeroes the counters between load-test runs so each run's numbers stand on their own
        // instead of accumulating across the whole process lifetime.
        group.MapPost("/metrics/reset", (ICacheMetrics metrics) =>
        {
            metrics.Reset();
            return Results.NoContent();
        });

        // Manual invalidation for the demo/load-test harness: evict a quote's cache entry
        // on demand (e.g. right before firing a concurrency burst) instead of waiting out the
        // 30s TTL, so the stampede scenario can be reproduced against a guaranteed-cold key.
        group.MapPost("/evict/{id:int}", async (int id, HybridCache cache, CancellationToken cancellationToken) =>
        {
            await cache.RemoveAsync(QuoteCacheKeys.ById(id), cancellationToken);
            return Results.NoContent();
        });
    }
}
