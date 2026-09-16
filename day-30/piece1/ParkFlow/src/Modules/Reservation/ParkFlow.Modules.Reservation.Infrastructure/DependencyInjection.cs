using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Reservation.Application.Abstractions;
using ParkFlow.Modules.Reservation.Infrastructure.BackgroundJobs;
using ParkFlow.Modules.Reservation.Infrastructure.Outbox;
using ParkFlow.Modules.Reservation.Infrastructure.Persistence;

namespace ParkFlow.Modules.Reservation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddReservationInfrastructure(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<ReservationDbContext>(configureDb);

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ReservationDbContext>());
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();

        services.AddHostedService<ReservationExpirationWorker>();

        return services;
    }
}
