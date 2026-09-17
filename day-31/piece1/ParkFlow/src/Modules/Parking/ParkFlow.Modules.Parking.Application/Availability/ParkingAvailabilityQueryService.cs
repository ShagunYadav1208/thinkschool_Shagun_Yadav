using ParkFlow.Modules.Parking.Application.Abstractions;

namespace ParkFlow.Modules.Parking.Application.Availability;

/// <summary>
/// Cache-aside read path: try the cache, and only hit the database on a miss, then repopulate the
/// cache. Never the other way around — nothing here ever treats the cache as authoritative or
/// writes application state through it.
/// </summary>
public sealed class ParkingAvailabilityQueryService(
    IParkingAvailabilityCache cache,
    IParkingSpotRepository spotRepository)
{
    private static readonly TimeSpan CacheFreshness = TimeSpan.FromSeconds(15);

    public async Task<ParkingAvailabilitySnapshot> GetAvailabilityAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        var cached = await cache.TryGetAsync(facilityId, cancellationToken);
        if (cached is not null && DateTimeOffset.UtcNow - cached.GeneratedAt < CacheFreshness)
        {
            return cached;
        }

        var availableCount = await spotRepository.CountAvailableAsync(facilityId, cancellationToken);
        var snapshot = new ParkingAvailabilitySnapshot(facilityId, availableCount, DateTimeOffset.UtcNow);

        await cache.SetAsync(snapshot, cancellationToken);

        return snapshot;
    }
}
