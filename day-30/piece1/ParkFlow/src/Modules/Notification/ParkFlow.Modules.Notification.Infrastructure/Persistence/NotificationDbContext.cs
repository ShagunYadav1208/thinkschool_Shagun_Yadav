using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Notification.Application.Abstractions;

namespace ParkFlow.Modules.Notification.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Notification.Domain.NotificationMessage;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Domain> Notifications => Set<Domain>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
    }

    public new Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        base.SaveChangesAsync(cancellationToken);
}
