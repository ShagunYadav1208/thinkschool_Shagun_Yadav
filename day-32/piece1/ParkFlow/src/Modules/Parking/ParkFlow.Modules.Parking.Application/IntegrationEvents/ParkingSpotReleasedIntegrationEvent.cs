using ParkFlow.BuildingBlocks.Application;

namespace ParkFlow.Modules.Parking.Application.IntegrationEvents;

/// <summary>Published after a spot goes back to Available — Notification can use it to alert anyone waiting on that facility.</summary>
public sealed record ParkingSpotReleasedIntegrationEvent(Guid ParkingSpotId, Guid FacilityId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
