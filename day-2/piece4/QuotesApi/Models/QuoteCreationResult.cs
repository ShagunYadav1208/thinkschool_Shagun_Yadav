namespace QuotesApi.Models;

public sealed record QuoteCreationResult(Quote? Quote, string? Error)
{
    public bool IsSuccess => Quote is not null;

    public static QuoteCreationResult Success(Quote quote) => new(quote, null);
    public static QuoteCreationResult Failure(string error) => new(null, error);
}
