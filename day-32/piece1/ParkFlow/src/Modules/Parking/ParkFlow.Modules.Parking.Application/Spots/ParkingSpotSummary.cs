namespace ParkFlow.Modules.Parking.Application.Spots;

/// <summary>Day 32: read-only projection for the frontend's spot grid.</summary>
public sealed record ParkingSpotSummary(
    Guid Id,
    Guid FacilityId,
    int FloorLevel,
    string SpotNumber,
    string SpotType,
    string Status);
