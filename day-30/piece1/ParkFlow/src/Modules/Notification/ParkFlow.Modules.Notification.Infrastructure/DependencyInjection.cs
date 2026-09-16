using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParkFlow.Modules.Notification.Application.Abstractions;
using ParkFlow.Modules.Notification.Infrastructure.Persistence;

namespace ParkFlow.Modules.Notification.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<NotificationDbContext>(configureDb);
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationDbContext>());
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationSender, ConsoleNotificationSender>();

        return services;
    }
}
