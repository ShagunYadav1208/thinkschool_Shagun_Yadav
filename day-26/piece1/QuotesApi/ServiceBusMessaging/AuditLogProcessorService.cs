using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace QuotesApi.ServiceBusMessaging;

/// <summary>
/// Two ServiceBusProcessor instances, both bound to the SAME "audit-log" subscription -
/// deliberately, to demonstrate the competing-consumer pattern for real rather than just assert
/// it: send a burst of quote events and the HandledBy field on the resulting audit log entries
/// shows work split across "audit-worker-1" and "audit-worker-2", not every message going to
/// both. A subscription is one logical queue; multiple processors reading it compete for messages
/// exactly the way multiple instances of a scaled-out consumer would in production.
///
/// Graceful shutdown mirrors Day 18's QueuedHostedService, just draining a different resource:
/// StopAsync calls StopProcessingAsync on both processors, which waits for any in-flight handler
/// call to finish (bounded by HostOptions.ShutdownTimeout, configured in InfrastructureExtensions)
/// before the connection is torn down.
/// </summary>
public class AuditLogProcessorService(
    ServiceBusClient client,
    ServiceBusOptions options,
    IEventLogStore<AuditLogEntry> store,
    ILogger<AuditLogProcessorService> logger) : BackgroundService
{
    private const int WorkerCount = 2;

    private readonly ProcessedMessageTracker _tracker = new();
    private readonly List<ServiceBusProcessor> _processors = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var i = 1; i <= WorkerCount; i++)
        {
            var workerId = $"audit-worker-{i}";
            var processor = client.CreateProcessor(
                options.TopicName,
                options.AuditLogSubscription,
                new ServiceBusProcessorOptions { MaxConcurrentCalls = 1, AutoCompleteMessages = false });

            processor.ProcessMessageAsync += args => HandleMessageAsync(args, workerId);
            processor.ProcessErrorAsync += args =>
            {
                logger.LogError(args.Exception, "{Worker} error on {EntityPath}", workerId, args.EntityPath);
                return Task.CompletedTask;
            };

            _processors.Add(processor);
            await processor.StartProcessingAsync(stoppingToken);
            logger.LogInformation(
                "{Worker} started, competing for messages on subscription {Subscription}.",
                workerId,
                options.AuditLogSubscription);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown - StopAsync below does the actual draining.
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args, string workerId)
    {
        var quoteEvent = JsonSerializer.Deserialize<QuoteCreatedEvent>(args.Message.Body)!;
        var isFirstDelivery = _tracker.TryMarkProcessed(args.Message.MessageId);

        store.Add(new AuditLogEntry
        {
            MessageId = args.Message.MessageId,
            QuoteId = quoteEvent.QuoteId,
            Author = quoteEvent.Author,
            HandledBy = workerId,
            WasDuplicate = !isFirstDelivery,
        });

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping audit-log processors, letting any in-flight handler finish.");

        foreach (var processor in _processors)
            await processor.StopProcessingAsync(cancellationToken);

        await base.StopAsync(cancellationToken);

        foreach (var processor in _processors)
            await processor.DisposeAsync();
    }
}
