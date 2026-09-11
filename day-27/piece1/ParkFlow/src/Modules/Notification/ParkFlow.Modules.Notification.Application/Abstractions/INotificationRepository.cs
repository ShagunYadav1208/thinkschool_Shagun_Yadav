namespace ParkFlow.Modules.Notification.Application.Abstractions;

using Domain = ParkFlow.Modules.Notification.Domain.NotificationMessage;

public interface INotificationRepository
{
    void Add(Domain message);
}
