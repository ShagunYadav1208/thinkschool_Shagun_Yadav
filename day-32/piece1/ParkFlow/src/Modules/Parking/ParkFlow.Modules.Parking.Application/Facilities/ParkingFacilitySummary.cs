namespace ParkFlow.Modules.Parking.Application.Facilities;

/// <summary>Day 32: read-only projection for the frontend's facility picker.</summary>
public sealed record ParkingFacilitySummary(Guid Id, string Name, string Address, int FloorCount);
