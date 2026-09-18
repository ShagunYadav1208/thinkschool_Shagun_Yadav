using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Reservation.Application.Abstractions;
using ParkFlow.Modules.Reservation.Domain;

namespace ParkFlow.Modules.Reservation.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Reservation.Domain.Reservation;

public sealed class ReservationRepository(ReservationDbContext dbContext, ReservationIdempotencyIndex idempotencyIndex) : IReservationRepository
{
    public Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Reservations.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <summary>
    /// Checks <see cref="ReservationIdempotencyIndex"/> first — see that class for why. A miss
    /// answers "not found" with no query; a hit resolves by primary key via FindAsync, which also
    /// returns an already-tracked instance for free if this same DbContext already loaded it this
    /// request (it never has here, but that's the point of using it over a fresh predicate query).
    /// </summary>
    public async Task<Domain?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (!idempotencyIndex.TryGetReservationId(idempotencyKey, out var reservationId))
        {
            return null;
        }

        return await dbContext.Reservations.FindAsync([reservationId], cancellationToken);
    }

    public Task<bool> HasOverlappingActiveReservationAsync(
        Guid parkingSpotId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default)
    {
        var activeStatuses = new[] { ReservationStatus.Pending, ReservationStatus.Confirmed, ReservationStatus.CheckedIn };

        return dbContext.Reservations.AnyAsync(
            r => r.ParkingSpotId == parkingSpotId
                 && activeStatuses.Contains(r.Status)
                 && r.StartTime < endTime
                 && startTime < r.EndTime,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Domain>> GetExpiredCandidatesAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
        await dbContext.Reservations
            .Where(r =>
                (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed)
                && r.EndTime < asOf)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Domain>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await dbContext.Reservations
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    // Recorded optimistically, before SaveChanges: this app's InMemory provider has no realistic
    // save-failure mode today (no unique constraints, no concurrency tokens, no external I/O), so
    // the alternative — an EF SaveChanges interceptor to record only on confirmed persistence — is
    // more machinery than this pass's actual risk justifies. Revisit if that stops being true.
    public void Add(Domain reservation)
    {
        dbContext.Reservations.Add(reservation);
        idempotencyIndex.Record(reservation.IdempotencyKey, reservation.Id);
    }
}
