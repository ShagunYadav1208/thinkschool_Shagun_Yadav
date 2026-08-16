namespace Quotes.Domain;

public sealed record User(string Id, string Email, string[] Scopes);
