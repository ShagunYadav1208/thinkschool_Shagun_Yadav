namespace Quotes.Domain;

public sealed record CreateQuoteRequest(string? Author, string? Text, string[]? Tags);
