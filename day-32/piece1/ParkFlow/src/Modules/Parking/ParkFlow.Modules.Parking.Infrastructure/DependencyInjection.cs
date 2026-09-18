using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Parking.Application.Abstractions;
using ParkFlow.Modules.Parking.Application.Availability;
using ParkFlow.Modules.Parking.Infrastructure.Caching;
using ParkFlow.Modules.Parking.Infrastructure.Outbox;
using ParkFlow.Modules.Parking.Infrastructure.Persistence;

namespace ParkFlow.Modules.Parking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddParkingInfrastructure(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<ParkingDbContext>(configureDb);
        services.AddMemoryCache();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ParkingDbContext>());
        services.AddScoped<IParkingSpotRepository, ParkingSpotRepository>();
        services.AddScoped<IParkingFacilityRepository, ParkingFacilityRepository>();
        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();

        // In-memory today; the interface is what makes swapping in HybridCache/Redis later a
        // one-line Infrastructure change instead of a rewrite (see README, "Caching Design").
        services.AddSingleton<IParkingAvailabilityCache, InMemoryParkingAvailabilityCache>();

        return services;
    }
}
