namespace QuotesApi.Models;

public sealed class CreateQuoteRequest
{
    public string? Author { get; set; }
    public string? Text { get; set; }
}
