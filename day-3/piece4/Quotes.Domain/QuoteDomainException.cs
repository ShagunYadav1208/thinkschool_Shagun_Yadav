namespace Quotes.Domain;

public sealed class QuoteDomainException(string message) : Exception(message);
