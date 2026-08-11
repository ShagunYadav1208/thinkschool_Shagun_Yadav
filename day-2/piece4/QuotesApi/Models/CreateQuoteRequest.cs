namespace QuotesApi.Models;

public sealed class CreateQuoteRequest
{
    public string? Author { get; init; }
    public string? Text { get; init; }
}
