using ParkFlow.Modules.Notification.Application.Abstractions;

namespace ParkFlow.Modules.Notification.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Notification.Domain.NotificationMessage;

public sealed class NotificationRepository(NotificationDbContext dbContext) : INotificationRepository
{
    public void Add(Domain message) => dbContext.Notifications.Add(message);
}
