using ParkFlow.Modules.Notification.Domain;

namespace ParkFlow.Modules.Notification.Application.Abstractions;

/// <summary>
/// Application-layer abstraction over "actually deliver this." Day 22's Infrastructure
/// implementation just logs; a real Infrastructure implementation would call an email/SMS/push
/// provider behind this same interface.
/// </summary>
public interface INotificationSender
{
    Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
