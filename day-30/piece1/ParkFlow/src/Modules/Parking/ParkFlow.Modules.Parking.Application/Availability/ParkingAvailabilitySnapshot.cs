namespace ParkFlow.Modules.Parking.Application.Availability;

/// <summary>What the cache actually stores — a point-in-time read, never the thing that's updated in place.</summary>
public sealed record ParkingAvailabilitySnapshot(Guid FacilityId, int AvailableSpotCount, DateTimeOffset GeneratedAt);
