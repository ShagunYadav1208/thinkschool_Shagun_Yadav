using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParkFlow.Modules.Vehicle.Application.Abstractions;
using ParkFlow.Modules.Vehicle.Infrastructure.Persistence;

namespace ParkFlow.Modules.Vehicle.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVehicleInfrastructure(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<VehicleDbContext>(configureDb);
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<VehicleDbContext>());
        services.AddScoped<IVehicleRepository, VehicleRepository>();

        return services;
    }
}
