using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public sealed class QuoteRepository(QuotesDbContext db) : IQuoteRepository
{
    public async Task<Quote> AddAsync(Quote quote, CancellationToken cancellationToken)
    {
        await db.Quotes.AddAsync(quote, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return quote;
    }

    public Task<Quote?> GetActiveByIdAsync(int id, CancellationToken cancellationToken) =>
        db.Quotes.FirstOrDefaultAsync(quote => quote.Id == id && !quote.IsDeleted, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
