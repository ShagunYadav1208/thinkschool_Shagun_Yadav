namespace ParkFlow.Modules.Reservation.Application.Reservations;

/// <summary>Day 32: read-only projection for the "my reservations" frontend view — never the aggregate itself.</summary>
public sealed record ReservationSummary(
    Guid Id,
    Guid VehicleId,
    Guid ParkingSpotId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    decimal Price,
    string Status);
