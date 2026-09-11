using ParkFlow.BuildingBlocks.Domain;

namespace ParkFlow.Modules.Vehicle.Domain;

/// <summary>
/// A vehicle registered by a user. Reservation only ever holds this aggregate's Id — never a
/// reference to it — so the two modules can evolve their vehicle/reservation models independently.
/// </summary>
public sealed class Vehicle : AggregateRoot<Guid>
{
    public Guid OwnerUserId { get; private set; }
    public string LicensePlate { get; private set; } = string.Empty;
    public VehicleType VehicleType { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }

    private Vehicle()
    {
        // EF Core materialization.
    }

    private Vehicle(Guid id, Guid ownerUserId, string licensePlate, VehicleType vehicleType, DateTimeOffset registeredAt) : base(id)
    {
        OwnerUserId = ownerUserId;
        LicensePlate = licensePlate;
        VehicleType = vehicleType;
        RegisteredAt = registeredAt;
    }

    public static Vehicle Register(Guid ownerUserId, string licensePlate, VehicleType vehicleType)
    {
        if (string.IsNullOrWhiteSpace(licensePlate))
        {
            throw new ArgumentException("A vehicle must have a license plate.", nameof(licensePlate));
        }

        return new Vehicle(Guid.NewGuid(), ownerUserId, licensePlate.Trim().ToUpperInvariant(), vehicleType, DateTimeOffset.UtcNow);
    }
}
