namespace Quotes.Domain;

public interface IQuoteService
{
    Task<Quote> CreateAsync(string? author, string? text, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}
