using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Parking.Application.Abstractions;
using ParkFlow.Modules.Parking.Domain;

namespace ParkFlow.Modules.Parking.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Parking.Domain.ParkingSpot;

public sealed class ParkingSpotRepository(ParkingDbContext dbContext) : IParkingSpotRepository
{
    public Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Spots.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<int> CountAvailableAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
        dbContext.Spots.CountAsync(
            s => s.FacilityId == facilityId && s.Status == ParkingSpotStatus.Available, cancellationToken);

    public void Add(Domain spot) => dbContext.Spots.Add(spot);
}
