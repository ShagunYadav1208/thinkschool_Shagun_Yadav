using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace QuotesApi.ServiceBusMessaging;

/// <summary>
/// Single processor on the "notifications" subscription. This is the dead-letter demonstration:
/// any event whose Text starts with the "POISON:" marker (see
/// JobEndpointExtensions/ServiceBusEndpointExtensions' /api/servicebus/poison) throws on every
/// delivery attempt, simulating a handler bug or a permanently-broken downstream dependency. The
/// exception is deliberately left uncaught here - ServiceBusProcessor abandons a message
/// automatically when the handler throws (incrementing its delivery count), regardless of the
/// AutoCompleteMessages setting, which only governs the successful-return path. Once the
/// subscription's MaxDeliveryCount (3, set at provisioning time - see infra/provision.md) is
/// exceeded, Service Bus itself moves the message to the subscription's dead-letter queue with
/// reason "MaxDeliveryCountExceeded" - no application code dead-letters it explicitly.
/// </summary>
public class NotificationProcessorService(
    ServiceBusClient client,
    ServiceBusOptions options,
    IEventLogStore<NotificationEntry> store,
    ILogger<NotificationProcessorService> logger) : BackgroundService
{
    private readonly ProcessedMessageTracker _tracker = new();
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = client.CreateProcessor(
            options.TopicName,
            options.NotificationsSubscription,
            new ServiceBusProcessorOptions { MaxConcurrentCalls = 1, AutoCompleteMessages = false });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "notifications processor error on {EntityPath}", args.EntityPath);
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("Notifications processor started on subscription {Subscription}.", options.NotificationsSubscription);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var quoteEvent = JsonSerializer.Deserialize<QuoteCreatedEvent>(args.Message.Body)!;

        if (quoteEvent.Text.StartsWith("POISON:", StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Poison event {MessageId} (delivery attempt {Count}) - throwing deliberately.",
                args.Message.MessageId,
                args.Message.DeliveryCount);

            throw new InvalidOperationException($"Simulated permanent failure processing event {args.Message.MessageId}.");
        }

        var isFirstDelivery = _tracker.TryMarkProcessed(args.Message.MessageId);

        store.Add(new NotificationEntry
        {
            MessageId = args.Message.MessageId,
            QuoteId = quoteEvent.QuoteId,
            Message = $"New quote by {quoteEvent.Author}: \"{quoteEvent.Text}\"",
            WasDuplicate = !isFirstDelivery,
        });

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping notifications processor, letting any in-flight handler finish.");

        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await base.StopAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
        else
        {
            await base.StopAsync(cancellationToken);
        }
    }
}
