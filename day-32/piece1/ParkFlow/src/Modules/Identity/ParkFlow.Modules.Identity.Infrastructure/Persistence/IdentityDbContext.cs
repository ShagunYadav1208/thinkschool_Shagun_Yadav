using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Identity.Application.Abstractions;

namespace ParkFlow.Modules.Identity.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Identity.Domain.User;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Domain> Users => Set<Domain>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }

    public new Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        base.SaveChangesAsync(cancellationToken);
}
