using ParkFlow.BuildingBlocks.Domain;

namespace ParkFlow.Modules.Parking.Domain;

/// <summary>
/// A single, individually reservable space. Its own aggregate root so its occupancy state can be
/// changed (and concurrency-checked) without touching the rest of the facility. Rule 1's "no
/// overlapping active reservations" is enforced by the Reservation module against its own data —
/// this aggregate only tracks the *current* physical state of the spot, which the Reservation and
/// Parking modules keep in sync via the integration events in the README's async flows.
/// </summary>
public sealed class ParkingSpot : AggregateRoot<Guid>
{
    public Guid FacilityId { get; private set; }
    public int FloorLevel { get; private set; }
    public string SpotNumber { get; private set; } = string.Empty;
    public ParkingSpotType SpotType { get; private set; }
    public ParkingSpotStatus Status { get; private set; }

    private ParkingSpot()
    {
        // EF Core materialization.
    }

    private ParkingSpot(Guid id, Guid facilityId, int floorLevel, string spotNumber, ParkingSpotType spotType) : base(id)
    {
        FacilityId = facilityId;
        FloorLevel = floorLevel;
        SpotNumber = spotNumber;
        SpotType = spotType;
        Status = ParkingSpotStatus.Available;
    }

    public static ParkingSpot Create(Guid facilityId, int floorLevel, string spotNumber, ParkingSpotType spotType)
    {
        if (string.IsNullOrWhiteSpace(spotNumber))
        {
            throw new ArgumentException("A parking spot must have a spot number.", nameof(spotNumber));
        }

        return new ParkingSpot(Guid.NewGuid(), facilityId, floorLevel, spotNumber, spotType);
    }

    /// <summary>Held for a reservation, but no vehicle has checked in yet.</summary>
    public void Reserve()
    {
        if (Status != ParkingSpotStatus.Available)
        {
            throw new InvalidOperationException($"Spot {Id} cannot be reserved while it is {Status}.");
        }

        Status = ParkingSpotStatus.Reserved;
    }

    /// <summary>A vehicle has physically checked in.</summary>
    public void Occupy()
    {
        if (Status != ParkingSpotStatus.Reserved)
        {
            throw new InvalidOperationException($"Spot {Id} cannot be occupied while it is {Status}.");
        }

        Status = ParkingSpotStatus.Occupied;
    }

    /// <summary>
    /// Rule 8: called after a reservation is completed, cancelled, expired, or marked as a no-show.
    /// </summary>
    public void Release()
    {
        if (Status == ParkingSpotStatus.OutOfService)
        {
            throw new InvalidOperationException($"Spot {Id} cannot be released while it is out of service.");
        }

        Status = ParkingSpotStatus.Available;
    }
}
