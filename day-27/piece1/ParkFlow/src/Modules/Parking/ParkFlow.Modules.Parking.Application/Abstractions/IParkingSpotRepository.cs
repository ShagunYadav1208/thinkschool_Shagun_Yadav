namespace ParkFlow.Modules.Parking.Application.Abstractions;

using Domain = ParkFlow.Modules.Parking.Domain.ParkingSpot;

public interface IParkingSpotRepository
{
    Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountAvailableAsync(Guid facilityId, CancellationToken cancellationToken = default);

    void Add(Domain spot);
}
