namespace QuotesIntegrationApi.Models;

public sealed record CreateQuoteRequest(string Author, string Text);

public sealed record TokenRequest(string Subject);
