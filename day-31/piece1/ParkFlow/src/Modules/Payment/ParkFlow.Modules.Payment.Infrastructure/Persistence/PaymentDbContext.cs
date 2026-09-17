using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Payment.Application.Abstractions;
using ParkFlow.Modules.Payment.Infrastructure.Outbox;

namespace ParkFlow.Modules.Payment.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Payment.Domain.Payment;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Domain> Payments => Set<Domain>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);
    }

    public new Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        base.SaveChangesAsync(cancellationToken);
}
