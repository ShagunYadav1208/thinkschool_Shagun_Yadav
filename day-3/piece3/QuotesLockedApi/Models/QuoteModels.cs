namespace QuotesLockedApi;

public sealed record CreateQuoteRequest(string Text);

public sealed record UpdateQuoteRequest(string Text);

public sealed record Quote(int Id, string Text, string OwnerId);
