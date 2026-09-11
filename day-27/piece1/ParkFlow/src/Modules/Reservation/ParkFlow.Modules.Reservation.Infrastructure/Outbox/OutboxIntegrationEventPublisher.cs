using System.Text.Json;
using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Reservation.Infrastructure.Persistence;

namespace ParkFlow.Modules.Reservation.Infrastructure.Outbox;

/// <summary>
/// Implements the Application-layer abstraction by adding a row to <see cref="ReservationDbContext"/>
/// — it does NOT call SaveChanges itself, so it lands in the same transaction as the aggregate
/// change that triggered it. A separate (not-yet-built, see README) dispatcher process is what
/// actually reads unprocessed rows and pushes them to the message broker.
/// </summary>
public sealed class OutboxIntegrationEventPublisher(ReservationDbContext dbContext) : IIntegrationEventPublisher
{
    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var message = new OutboxMessage
        {
            Id = integrationEvent.EventId,
            Type = integrationEvent.GetType().FullName ?? integrationEvent.GetType().Name,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            OccurredOn = integrationEvent.OccurredOn
        };

        dbContext.OutboxMessages.Add(message);
        return Task.CompletedTask;
    }
}
