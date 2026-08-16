namespace Quotes.Domain;

public interface IQuoteRepository
{
    Task<Quote> AddAsync(Quote quote, CancellationToken cancellationToken);

    Task<Quote?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task UpdateAsync(Quote quote, CancellationToken cancellationToken);
}
