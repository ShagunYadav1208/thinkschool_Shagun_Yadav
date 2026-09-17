using System.Collections.Concurrent;

namespace ParkFlow.Modules.Reservation.Infrastructure.Persistence;

/// <summary>
/// Day 31 perf pass: <see cref="ReservationRepository.GetByIdempotencyKeyAsync"/> is called on
/// every single <c>Create</c> request, and the overwhelmingly common case is "no reservation has
/// ever used this key" — a fresh <see cref="Guid"/> a client generated for this one request. Under
/// the EF Core InMemory provider, a predicate-based query like <c>SingleOrDefaultAsync</c> scans
/// every row in the table with no index to shortcut it, so that common case gets linearly slower as
/// the table grows (see day-31/piece1/README.md's perf section for the measured before/after).
///
/// This is a singleton, in-process index of every idempotency key this instance has actually
/// persisted, checked before ever touching the DbContext: a miss here means "definitely not in the
/// table" with no query at all (O(1) instead of O(n)); a hit means "look it up by primary key",
/// which EF Core's <c>FindAsync</c> resolves without a table scan. It only ever grows and only
/// lives as long as the process — exactly matching the InMemory database's own lifetime, so it
/// can't drift out of sync with a real persisted store swapped in later (that swap removes this
/// class entirely, since a real database gets a proper unique index instead).
/// </summary>
public sealed class ReservationIdempotencyIndex
{
    private readonly ConcurrentDictionary<Guid, Guid> _reservationIdsByKey = new();

    public bool TryGetReservationId(Guid idempotencyKey, out Guid reservationId) =>
        _reservationIdsByKey.TryGetValue(idempotencyKey, out reservationId);

    public void Record(Guid idempotencyKey, Guid reservationId) =>
        _reservationIdsByKey[idempotencyKey] = reservationId;
}
