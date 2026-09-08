namespace QuotesApi.ServiceBusMessaging;

/// <summary>
/// Published to the "quote-events" topic on every successful quote creation. EventId becomes the
/// Service Bus message's MessageId - it's the idempotency key every subscriber dedupes on (not
/// QuoteId), because the same domain event can legitimately be re-sent - a publisher retry, or the
/// deliberate /api/servicebus/replay/{quoteId} demo below - and every subscriber needs to recognize
/// "I've already handled this exact delivery" independent of which quote it's about.
/// </summary>
public record QuoteCreatedEvent(Guid EventId, int QuoteId, string Author, string Text, DateTimeOffset CreatedAt);
