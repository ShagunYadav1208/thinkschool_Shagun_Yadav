using Microsoft.Extensions.DependencyInjection;
using ParkFlow.Modules.Parking.Application.Availability;
using ParkFlow.Modules.Parking.Application.Spots;

namespace ParkFlow.Modules.Parking.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddParkingApplication(this IServiceCollection services) => services
        .AddScoped<ParkingAvailabilityQueryService>()
        .AddScoped<ParkingSpotApplicationService>();
}
