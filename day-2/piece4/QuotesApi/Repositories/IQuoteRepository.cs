using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IQuoteRepository
{
    Task<Quote> AddAsync(Quote quote, CancellationToken cancellationToken);
    Task<Quote?> GetActiveByIdAsync(int id, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
