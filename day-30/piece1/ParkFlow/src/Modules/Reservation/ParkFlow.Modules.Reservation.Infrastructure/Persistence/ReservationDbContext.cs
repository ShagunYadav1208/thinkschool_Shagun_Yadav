using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Reservation.Application.Abstractions;
using ParkFlow.Modules.Reservation.Infrastructure.Outbox;

namespace ParkFlow.Modules.Reservation.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Reservation.Domain.Reservation;

/// <summary>
/// The Reservation module's own schema. Other modules never query this DbContext directly — they
/// only ever learn about reservations through integration events on the outbox below.
/// </summary>
public sealed class ReservationDbContext(DbContextOptions<ReservationDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Domain> Reservations => Set<Domain>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReservationDbContext).Assembly);
    }

    public new Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        base.SaveChangesAsync(cancellationToken);
}
