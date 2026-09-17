using ParkFlow.BuildingBlocks.Application;

namespace ParkFlow.Modules.Reservation.Application.IntegrationEvents;

/// <summary>
/// Flow 4: published after Reservation.Cancel(); the Parking module reacts by releasing the spot.
/// </summary>
public sealed record ReservationCancelledIntegrationEvent(
    Guid ReservationId,
    Guid ParkingSpotId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
