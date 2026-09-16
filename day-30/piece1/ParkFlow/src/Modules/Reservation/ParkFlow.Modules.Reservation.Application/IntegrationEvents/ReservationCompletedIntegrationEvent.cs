using ParkFlow.BuildingBlocks.Application;

namespace ParkFlow.Modules.Reservation.Application.IntegrationEvents;

/// <summary>
/// Flow 3: published after vehicle exit / Reservation.Complete(); Payment charges for the stay.
/// </summary>
public sealed record ReservationCompletedIntegrationEvent(
    Guid ReservationId,
    Guid ParkingSpotId,
    DateTimeOffset CheckedOutAt) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
