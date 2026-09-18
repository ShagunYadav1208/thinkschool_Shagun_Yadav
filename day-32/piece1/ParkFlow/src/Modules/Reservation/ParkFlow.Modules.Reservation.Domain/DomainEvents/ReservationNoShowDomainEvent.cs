using ParkFlow.BuildingBlocks.Domain;

namespace ParkFlow.Modules.Reservation.Domain.DomainEvents;

public sealed record ReservationNoShowDomainEvent(
    Guid ReservationId,
    Guid ParkingSpotId) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
