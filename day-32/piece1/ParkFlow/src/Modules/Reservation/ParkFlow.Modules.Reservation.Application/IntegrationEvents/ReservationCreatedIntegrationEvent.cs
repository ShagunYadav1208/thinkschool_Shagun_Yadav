using ParkFlow.BuildingBlocks.Application;

namespace ParkFlow.Modules.Reservation.Application.IntegrationEvents;

/// <summary>
/// Flow 1: published via the outbox after the reservation is persisted. The Payment module reads
/// it to pre-authorize a charge; the Notification module reads it to send a confirmation.
/// </summary>
public sealed record ReservationCreatedIntegrationEvent(
    Guid ReservationId,
    Guid UserId,
    Guid VehicleId,
    Guid ParkingSpotId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    decimal Price) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
