using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Parking.Application.Abstractions;
using ParkFlow.Modules.Parking.Domain;
using ParkFlow.Modules.Parking.Infrastructure.Outbox;

namespace ParkFlow.Modules.Parking.Infrastructure.Persistence;

public sealed class ParkingDbContext(DbContextOptions<ParkingDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<ParkingFacility> Facilities => Set<ParkingFacility>();
    public DbSet<ParkingSpot> Spots => Set<ParkingSpot>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ParkingDbContext).Assembly);
    }

    public new Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        base.SaveChangesAsync(cancellationToken);
}
