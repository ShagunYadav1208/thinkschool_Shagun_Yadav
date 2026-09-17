using Microsoft.Extensions.DependencyInjection;
using ParkFlow.Modules.Notification.Application.Notifications;

namespace ParkFlow.Modules.Notification.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services) =>
        services.AddScoped<NotificationApplicationService>();
}
