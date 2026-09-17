using Microsoft.Extensions.DependencyInjection;
using ParkFlow.Modules.Reservation.Application.Reservations;

namespace ParkFlow.Modules.Reservation.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddReservationApplication(this IServiceCollection services) =>
        services.AddScoped<ReservationApplicationService>();
}
