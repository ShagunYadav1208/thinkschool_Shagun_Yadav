using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Vehicle.Application.Abstractions;

namespace ParkFlow.Modules.Vehicle.Application.Vehicles;

using Domain = ParkFlow.Modules.Vehicle.Domain.Vehicle;

public sealed class VehicleApplicationService(IVehicleRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<Result<Guid>> RegisterAsync(RegisterVehicleRequest request, CancellationToken cancellationToken = default)
    {
        if (await repository.ExistsWithLicensePlateAsync(request.LicensePlate, cancellationToken))
        {
            return Result.Failure<Guid>("A vehicle with that license plate is already registered.");
        }

        var vehicle = Domain.Register(request.OwnerUserId, request.LicensePlate, request.VehicleType);
        repository.Add(vehicle);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(vehicle.Id);
    }
}
