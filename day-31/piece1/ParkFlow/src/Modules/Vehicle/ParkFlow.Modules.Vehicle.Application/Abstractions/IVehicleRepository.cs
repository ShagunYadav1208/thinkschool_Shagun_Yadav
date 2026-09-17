namespace ParkFlow.Modules.Vehicle.Application.Abstractions;

using Domain = ParkFlow.Modules.Vehicle.Domain.Vehicle;

public interface IVehicleRepository
{
    Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsWithLicensePlateAsync(string licensePlate, CancellationToken cancellationToken = default);

    void Add(Domain vehicle);
}
