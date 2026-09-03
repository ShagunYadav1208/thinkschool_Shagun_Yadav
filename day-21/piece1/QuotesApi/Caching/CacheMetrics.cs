namespace QuotesApi.Caching;

public class CacheMetrics : ICacheMetrics
{
    private long _dbReads;
    private long _cacheRequests;
    private long _cacheMisses;

    public long DbReads => Interlocked.Read(ref _dbReads);
    public long CacheRequests => Interlocked.Read(ref _cacheRequests);
    public long CacheMisses => Interlocked.Read(ref _cacheMisses);

    public void RecordDbRead() => Interlocked.Increment(ref _dbReads);
    public void RecordCacheRequest() => Interlocked.Increment(ref _cacheRequests);
    public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);

    public void Reset()
    {
        Interlocked.Exchange(ref _dbReads, 0);
        Interlocked.Exchange(ref _cacheRequests, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
    }
}
