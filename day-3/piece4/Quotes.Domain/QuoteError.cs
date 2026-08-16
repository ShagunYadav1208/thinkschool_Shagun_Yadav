namespace Quotes.Domain;

public sealed record QuoteError(string Field, string Message);
