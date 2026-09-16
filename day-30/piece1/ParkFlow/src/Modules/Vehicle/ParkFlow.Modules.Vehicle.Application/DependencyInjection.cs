using Microsoft.Extensions.DependencyInjection;
using ParkFlow.Modules.Vehicle.Application.Vehicles;

namespace ParkFlow.Modules.Vehicle.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddVehicleApplication(this IServiceCollection services) =>
        services.AddScoped<VehicleApplicationService>();
}
