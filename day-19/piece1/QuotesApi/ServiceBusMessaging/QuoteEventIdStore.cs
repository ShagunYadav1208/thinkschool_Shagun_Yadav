using System.Collections.Concurrent;

namespace QuotesApi.ServiceBusMessaging;

/// <summary>
/// Remembers the last QuoteCreatedEvent published for each quote id, purely so
/// POST /api/servicebus/replay/{quoteId} has something to re-send with the identical MessageId -
/// this is test-harness state for the idempotency demo, not a real event store.
/// </summary>
public interface IQuoteEventIdStore
{
    void Remember(QuoteCreatedEvent quoteEvent);
    QuoteCreatedEvent? GetLastEvent(int quoteId);
}

public class QuoteEventIdStore : IQuoteEventIdStore
{
    private readonly ConcurrentDictionary<int, QuoteCreatedEvent> _lastEventByQuoteId = new();

    public void Remember(QuoteCreatedEvent quoteEvent) => _lastEventByQuoteId[quoteEvent.QuoteId] = quoteEvent;

    public QuoteCreatedEvent? GetLastEvent(int quoteId) => _lastEventByQuoteId.GetValueOrDefault(quoteId);
}
