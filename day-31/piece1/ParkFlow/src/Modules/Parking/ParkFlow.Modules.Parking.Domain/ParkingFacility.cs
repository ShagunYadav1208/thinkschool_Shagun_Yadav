using ParkFlow.BuildingBlocks.Domain;

namespace ParkFlow.Modules.Parking.Domain;

/// <summary>
/// A physical parking facility (garage/lot). Spots are their own aggregate (see
/// <see cref="ParkingSpot"/>) rather than children collected here, since spots change occupancy
/// state constantly and independently — nesting them under Facility would force every check-in to
/// load and re-save the whole facility.
/// </summary>
public sealed class ParkingFacility : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public int FloorCount { get; private set; }

    private ParkingFacility()
    {
        // EF Core materialization.
    }

    private ParkingFacility(Guid id, string name, string address, int floorCount) : base(id)
    {
        Name = name;
        Address = address;
        FloorCount = floorCount;
    }

    public static ParkingFacility Create(string name, string address, int floorCount)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A facility must have a name.", nameof(name));
        }

        if (floorCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(floorCount), "A facility must have at least one floor.");
        }

        return new ParkingFacility(Guid.NewGuid(), name, address, floorCount);
    }
}
