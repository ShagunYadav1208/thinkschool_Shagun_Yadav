namespace ParkFlow.BuildingBlocks.Application;

/// <summary>
/// Application-layer abstraction over "write this integration event to the outbox as part of the
/// current database transaction." Each module's Infrastructure layer provides the real EF Core
/// implementation; Application code never talks to the outbox table or the message broker directly.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
