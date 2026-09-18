using System.Text.Json;
using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Parking.Infrastructure.Persistence;

namespace ParkFlow.Modules.Parking.Infrastructure.Outbox;

public sealed class OutboxIntegrationEventPublisher(ParkingDbContext dbContext) : IIntegrationEventPublisher
{
    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            Type = integrationEvent.GetType().FullName ?? integrationEvent.GetType().Name,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            OccurredOn = integrationEvent.OccurredOn
        });

        return Task.CompletedTask;
    }
}
