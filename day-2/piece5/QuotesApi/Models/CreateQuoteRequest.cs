namespace QuotesApi.Models;

public sealed class CreateQuoteRequest
{
    public string Author { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
}
