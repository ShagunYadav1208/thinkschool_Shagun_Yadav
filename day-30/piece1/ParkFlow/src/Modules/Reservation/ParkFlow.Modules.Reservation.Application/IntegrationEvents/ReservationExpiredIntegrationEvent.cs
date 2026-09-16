using ParkFlow.BuildingBlocks.Application;

namespace ParkFlow.Modules.Reservation.Application.IntegrationEvents;

/// <summary>
/// Flow 2: published by the expiration background worker; Parking releases the spot and
/// Notification tells the driver.
/// </summary>
public sealed record ReservationExpiredIntegrationEvent(
    Guid ReservationId,
    Guid ParkingSpotId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
