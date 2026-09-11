namespace ParkFlow.Modules.Parking.Application.Availability;

/// <summary>
/// The architectural boundary from the README's caching section: API -> Application ->
/// AvailabilityQueryService -> this cache -> database fallback. The Infrastructure implementation
/// (Day 22: a trivial in-memory cache; later: HybridCache backed by Redis) is swapped in behind
/// this interface without the Application layer ever knowing. The database, not this cache, stays
/// the source of truth — a cache miss or a stale/evicted entry must always be safe to fall through
/// to the repository.
/// </summary>
public interface IParkingAvailabilityCache
{
    Task<ParkingAvailabilitySnapshot?> TryGetAsync(Guid facilityId, CancellationToken cancellationToken = default);

    Task SetAsync(ParkingAvailabilitySnapshot snapshot, CancellationToken cancellationToken = default);

    Task InvalidateAsync(Guid facilityId, CancellationToken cancellationToken = default);
}
