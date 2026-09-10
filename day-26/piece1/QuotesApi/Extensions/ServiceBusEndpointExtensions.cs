using QuotesApi.ServiceBusMessaging;

namespace QuotesApi.Extensions;

public static class ServiceBusEndpointExtensions
{
    public static void MapServiceBusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/servicebus");

        group.MapGet("/audit-log", (IEventLogStore<AuditLogEntry> store) =>
            Results.Ok(store.GetAll().OrderByDescending(e => e.HandledAt)));

        group.MapGet("/notifications", (IEventLogStore<NotificationEntry> store) =>
            Results.Ok(store.GetAll().OrderByDescending(e => e.HandledAt)));

        group.MapGet("/dlq", async (IDeadLetterInspector inspector, CancellationToken cancellationToken) =>
            Results.Ok(await inspector.PeekNotificationsDeadLetterAsync(cancellationToken)));

        // Deliberately re-publishes the SAME MessageId as the last event for this quote -
        // simulates an upstream publisher retry. Proves it's the subscriber-side
        // ProcessedMessageTracker (not Service Bus's own duplicate detection, which is disabled on
        // this topic - see infra/provision.md) that prevents double-processing: the audit log and
        // notifications lists both grow to reflect one legitimate duplicate delivery, but no
        // second real "handling" happens.
        group.MapPost("/replay/{quoteId:int}", async (
            int quoteId,
            IQuoteEventPublisher publisher,
            IQuoteEventIdStore eventIds,
            CancellationToken cancellationToken) =>
        {
            var lastEvent = eventIds.GetLastEvent(quoteId);
            if (lastEvent is null)
                return Results.NotFound($"No event has been published yet for quote {quoteId}.");

            await publisher.PublishAsync(lastEvent, cancellationToken);
            return Results.Accepted(value: lastEvent);
        });

        // Publishes a deliberately-poisoned event, not tied to a real quote, so the notifications
        // processor's failure path - and its eventual dead-lettering after MaxDeliveryCount is
        // exceeded - can be demonstrated on demand.
        group.MapPost("/poison", async (IQuoteEventPublisher publisher, CancellationToken cancellationToken) =>
        {
            var poisonEvent = new QuoteCreatedEvent(
                Guid.NewGuid(),
                QuoteId: -1,
                Author: "Poison Test",
                Text: $"POISON: deliberately unprocessable event created at {DateTimeOffset.UtcNow:O}",
                CreatedAt: DateTimeOffset.UtcNow);

            await publisher.PublishAsync(poisonEvent, cancellationToken);
            return Results.Accepted(value: poisonEvent);
        });
    }
}
