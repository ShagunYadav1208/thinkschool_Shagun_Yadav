using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Caching;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class QuoteRepository(
    QuotesDbContext db,
    ICacheMetrics cacheMetrics,
    IOptions<CacheDemoOptions> cacheDemoOptions) : IQuoteRepository
{
    public async Task<List<Quote>> GetPagedAsync(
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        return await db.Quotes
            .AsNoTracking()
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
    }

    // Day 21: the hot read HybridCache sits in front of (see QuoteEndpointExtensions'
    // GET /{id} and /{id}/uncached). Every real call to this method - whether it arrived
    // through the cache's factory on a miss, or through the uncached comparison endpoint -
    // is counted here, so ICacheMetrics.DbReads is an honest measure of DB load regardless
    // of which path a caller took.
    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        cacheMetrics.RecordDbRead();

        var simulatedLatencyMs = cacheDemoOptions.Value.SimulatedDbLatencyMs;
        if (simulatedLatencyMs > 0)
            await Task.Delay(simulatedLatencyMs, cancellationToken);

        return await db.Quotes
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
    }

    public async Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(cancellationToken);
        return quote;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await db.Quotes
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

        if (quote is null)
            return false;

        db.Quotes.Remove(quote);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}