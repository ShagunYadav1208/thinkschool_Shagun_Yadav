using ParkFlow.BuildingBlocks.Domain;

namespace ParkFlow.Modules.Reservation.Domain.DomainEvents;

public sealed record ReservationCreatedDomainEvent(
    Guid ReservationId,
    Guid UserId,
    Guid VehicleId,
    Guid ParkingSpotId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    decimal Price,
    Guid IdempotencyKey) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
