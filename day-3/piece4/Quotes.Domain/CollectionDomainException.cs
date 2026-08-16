namespace Quotes.Domain;

public sealed class CollectionDomainException(string message) : Exception(message);
