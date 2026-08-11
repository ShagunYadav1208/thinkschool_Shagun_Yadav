using QuotesApi.Models;

namespace QuotesApi.Services;

public interface IQuoteService
{
    Task<Quote> CreateAsync(CreateQuoteRequest request, CancellationToken cancellationToken);
}
