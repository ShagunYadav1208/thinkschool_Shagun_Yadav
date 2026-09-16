using Microsoft.Extensions.Logging;
using ParkFlow.Modules.Notification.Application.Abstractions;
using ParkFlow.Modules.Notification.Domain;

namespace ParkFlow.Modules.Notification.Infrastructure;

/// <summary>
/// Day 22 stand-in for a real email/SMS/push provider: it "delivers" by logging. Swapping this for
/// a real provider later is purely an Infrastructure change behind <see cref="INotificationSender"/>.
/// </summary>
public sealed class ConsoleNotificationSender(ILogger<ConsoleNotificationSender> logger) : INotificationSender
{
    public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[{Channel}] Notification {Type} to user {RecipientUserId}: {Body}",
            message.Channel, message.Type, message.RecipientUserId, message.Body);

        return Task.FromResult(true);
    }
}
