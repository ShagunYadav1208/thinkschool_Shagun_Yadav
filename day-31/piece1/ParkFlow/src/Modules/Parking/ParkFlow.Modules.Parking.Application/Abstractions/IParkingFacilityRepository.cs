namespace ParkFlow.Modules.Parking.Application.Abstractions;

using Domain = ParkFlow.Modules.Parking.Domain.ParkingFacility;

public interface IParkingFacilityRepository
{
    Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Domain facility);
}
