namespace ParkFlow.Modules.Reservation.Application.Abstractions;

using Domain = ParkFlow.Modules.Reservation.Domain.Reservation;

/// <summary>
/// Defined here (Application), implemented in Infrastructure with EF Core — the Dependency
/// Inversion half of Clean Architecture: the inner layer owns the contract, the outer layer
/// depends on it, never the other way round.
/// </summary>
public interface IReservationRepository
{
    Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Domain?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rule 1: overlap detection ultimately needs a database-level check (or constraint) against
    /// other active reservations for the same spot — this is the seam where that query would live.
    /// </summary>
    Task<bool> HasOverlappingActiveReservationAsync(
        Guid parkingSpotId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Domain>> GetExpiredCandidatesAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    void Add(Domain reservation);
}
