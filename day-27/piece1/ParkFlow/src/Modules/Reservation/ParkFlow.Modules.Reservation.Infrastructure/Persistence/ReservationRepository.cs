using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Reservation.Application.Abstractions;
using ParkFlow.Modules.Reservation.Domain;

namespace ParkFlow.Modules.Reservation.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Reservation.Domain.Reservation;

public sealed class ReservationRepository(ReservationDbContext dbContext) : IReservationRepository
{
    public Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Reservations.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Domain?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken = default) =>
        dbContext.Reservations.SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

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

    public void Add(Domain reservation) => dbContext.Reservations.Add(reservation);
}
