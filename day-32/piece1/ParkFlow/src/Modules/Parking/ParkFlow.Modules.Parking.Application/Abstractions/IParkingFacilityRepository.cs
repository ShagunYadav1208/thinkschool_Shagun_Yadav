namespace ParkFlow.Modules.Parking.Application.Abstractions;

using Domain = ParkFlow.Modules.Parking.Domain.ParkingFacility;

public interface IParkingFacilityRepository
{
    Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Day 32: powers the frontend's facility picker.</summary>
    Task<IReadOnlyList<Domain>> GetAllAsync(CancellationToken cancellationToken = default);

    void Add(Domain facility);
}
