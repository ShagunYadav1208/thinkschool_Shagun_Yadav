using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace QuotesApi.ServiceBusMessaging;

/// <summary>
/// One ServiceBusSender per process, reused across requests (senders are meant to be long-lived
/// and are safe for concurrent use - creating one per publish would be needlessly expensive).
/// MessageId is set to the event's own EventId, not left to Service Bus to generate, specifically
/// so /api/servicebus/replay/{quoteId} can re-send the exact same MessageId later and prove
/// subscriber-side dedup rather than Service Bus's own (unused here) duplicate detection.
/// </summary>
public class QuoteEventPublisher : IQuoteEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public QuoteEventPublisher(ServiceBusClient client, ServiceBusOptions options)
    {
        _sender = client.CreateSender(options.TopicName);
    }

    public async Task PublishAsync(QuoteCreatedEvent quoteEvent, CancellationToken cancellationToken)
    {
        var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(quoteEvent))
        {
            MessageId = quoteEvent.EventId.ToString(),
            ContentType = "application/json",
            Subject = "QuoteCreated",
        };

        await _sender.SendMessageAsync(message, cancellationToken);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
