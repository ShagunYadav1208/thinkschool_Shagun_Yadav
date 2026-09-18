using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Parking.Application.Abstractions;

namespace ParkFlow.Modules.Parking.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Parking.Domain.ParkingFacility;

public sealed class ParkingFacilityRepository(ParkingDbContext dbContext) : IParkingFacilityRepository
{
    public Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Facilities.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

    public void Add(Domain facility) => dbContext.Facilities.Add(facility);
}
