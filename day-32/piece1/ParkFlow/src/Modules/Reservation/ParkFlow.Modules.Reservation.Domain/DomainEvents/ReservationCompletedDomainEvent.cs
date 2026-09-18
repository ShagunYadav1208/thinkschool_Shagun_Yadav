using ParkFlow.BuildingBlocks.Domain;

namespace ParkFlow.Modules.Reservation.Domain.DomainEvents;

public sealed record ReservationCompletedDomainEvent(
    Guid ReservationId,
    Guid ParkingSpotId,
    DateTimeOffset CheckedOutAt) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
