using ParkFlow.Modules.Parking.Application.Abstractions;

namespace ParkFlow.Modules.Parking.Application.Facilities;

/// <summary>Day 32: read-only facility directory for the frontend — never mutates state.</summary>
public sealed class ParkingFacilityApplicationService(IParkingFacilityRepository facilityRepository)
{
    public async Task<IReadOnlyList<ParkingFacilitySummary>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var facilities = await facilityRepository.GetAllAsync(cancellationToken);

        return facilities
            .Select(f => new ParkingFacilitySummary(f.Id, f.Name, f.Address, f.FloorCount))
            .ToList();
    }
}
