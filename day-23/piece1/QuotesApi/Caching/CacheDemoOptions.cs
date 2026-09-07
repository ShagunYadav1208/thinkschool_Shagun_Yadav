namespace QuotesApi.Caching;

/// <summary>
/// SimulatedDbLatencyMs stands in for "this row is mildly expensive to fetch" (a join, a
/// computed column, a slow disk) - SQLite reading one row by primary key is sub-millisecond
/// on its own, which would make a concurrent stampede resolve inside a single tick and be
/// unobservable under load. The delay widens that window so N concurrent misses on a cold
/// key can actually be seen racing each other before the first one populates the cache.
/// </summary>
public class CacheDemoOptions
{
    public const string SectionName = "CacheDemo";

    public int SimulatedDbLatencyMs { get; set; } = 150;
}
