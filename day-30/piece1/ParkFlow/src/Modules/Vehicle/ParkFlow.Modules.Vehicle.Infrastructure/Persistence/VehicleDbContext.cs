using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Vehicle.Application.Abstractions;

namespace ParkFlow.Modules.Vehicle.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Vehicle.Domain.Vehicle;

public sealed class VehicleDbContext(DbContextOptions<VehicleDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Domain> Vehicles => Set<Domain>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VehicleDbContext).Assembly);
    }

    public new Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        base.SaveChangesAsync(cancellationToken);
}
