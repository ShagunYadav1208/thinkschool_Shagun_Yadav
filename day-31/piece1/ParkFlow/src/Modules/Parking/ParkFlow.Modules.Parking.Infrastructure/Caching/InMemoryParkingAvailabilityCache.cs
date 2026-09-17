using Microsoft.Extensions.Caching.Memory;
using ParkFlow.Modules.Parking.Application.Availability;

namespace ParkFlow.Modules.Parking.Infrastructure.Caching;

/// <summary>
/// Day 22's implementation of the caching boundary: a real, working in-process cache (not a fake),
/// just not the distributed one this is meant to become. Swapping this for a HybridCache-backed
/// implementation (in-memory L1 + Redis L2) later is purely an Infrastructure change — nothing in
/// Application or the API needs to know, because both sit behind <see cref="IParkingAvailabilityCache"/>.
/// </summary>
public sealed class InMemoryParkingAvailabilityCache(IMemoryCache memoryCache) : IParkingAvailabilityCache
{
    private static readonly TimeSpan CacheEntryLifetime = TimeSpan.FromSeconds(30);

    public Task<ParkingAvailabilitySnapshot?> TryGetAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(memoryCache.TryGetValue(CacheKey(facilityId), out ParkingAvailabilitySnapshot? snapshot)
            ? snapshot
            : null);

    public Task SetAsync(ParkingAvailabilitySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        memoryCache.Set(CacheKey(snapshot.FacilityId), snapshot, CacheEntryLifetime);
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        memoryCache.Remove(CacheKey(facilityId));
        return Task.CompletedTask;
    }

    private static string CacheKey(Guid facilityId) => $"parking:availability:{facilityId}";
}
