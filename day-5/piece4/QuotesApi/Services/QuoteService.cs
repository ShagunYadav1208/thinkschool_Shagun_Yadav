using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Services;

public sealed class QuoteService(IQuoteRepository repository, IClock clock) : IQuoteService
{
    public Task<Quote> CreateAsync(
        CreateQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var quote = new Quote
        {
            Author = request.Author.Trim(),
            Text = request.Text.Trim(),
            CreatedAt = clock.UtcNow
        };

        return repository.AddAsync(quote, cancellationToken);
    }
}
