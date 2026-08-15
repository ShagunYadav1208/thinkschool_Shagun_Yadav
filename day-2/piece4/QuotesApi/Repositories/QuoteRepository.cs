using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class QuoteRepository(QuotesDbContext db) : IQuoteRepository
{
    public async Task<List<Quote>> GetPagedAsync(
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        return await db.Quotes
            .AsNoTracking()
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
    }

    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await db.Quotes
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);
    }

    public async Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(cancellationToken);
        return quote;
    }

    public async Task UpdateAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        db.Quotes.Update(quote);
        await db.SaveChangesAsync(cancellationToken);
    }
}
