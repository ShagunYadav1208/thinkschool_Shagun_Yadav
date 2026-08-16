namespace Quotes.Domain;

public sealed class QuoteService(IQuoteRepository repository, IClock clock) : IQuoteService
{
    public Task<Quote> CreateAsync(
        string? author,
        string? text,
        CancellationToken cancellationToken)
    {
        var result = Quote.Create(author, text, clock.UtcNow);

        if (!result.IsSuccess)
        {
            throw new QuoteValidationException(result.Errors);
        }

        return repository.AddAsync(result.Value!, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await repository.GetByIdAsync(id, cancellationToken);

        if (quote is null)
        {
            return false;
        }

        quote.Delete();
        await repository.UpdateAsync(quote, cancellationToken);
        return true;
    }
}
