using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Vehicle.Application.Abstractions;

namespace ParkFlow.Modules.Vehicle.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Vehicle.Domain.Vehicle;

public sealed class VehicleRepository(VehicleDbContext dbContext) : IVehicleRepository
{
    public Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Vehicles.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<bool> ExistsWithLicensePlateAsync(string licensePlate, CancellationToken cancellationToken = default) =>
        dbContext.Vehicles.AnyAsync(v => v.LicensePlate == licensePlate.Trim().ToUpper(), cancellationToken);

    public void Add(Domain vehicle) => dbContext.Vehicles.Add(vehicle);
}
