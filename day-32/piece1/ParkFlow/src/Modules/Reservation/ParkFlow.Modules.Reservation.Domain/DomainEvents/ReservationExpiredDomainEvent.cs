using ParkFlow.BuildingBlocks.Domain;

namespace ParkFlow.Modules.Reservation.Domain.DomainEvents;

public sealed record ReservationExpiredDomainEvent(
    Guid ReservationId,
    Guid ParkingSpotId) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
