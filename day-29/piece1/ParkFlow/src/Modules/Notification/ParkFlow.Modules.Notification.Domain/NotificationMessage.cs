using ParkFlow.BuildingBlocks.Domain;

namespace ParkFlow.Modules.Notification.Domain;

/// <summary>
/// One outbound message to one recipient. Kept for delivery history/retries — the actual
/// send is performed by an <c>INotificationSender</c> in the Application layer, this aggregate
/// just records the attempt and its outcome.
/// </summary>
public sealed class NotificationMessage : AggregateRoot<Guid>
{
    public Guid RecipientUserId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationType Type { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public NotificationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    private NotificationMessage()
    {
        // EF Core materialization.
    }

    private NotificationMessage(Guid id, Guid recipientUserId, NotificationChannel channel, NotificationType type, string body) : base(id)
    {
        RecipientUserId = recipientUserId;
        Channel = channel;
        Type = type;
        Body = body;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static NotificationMessage Create(Guid recipientUserId, NotificationChannel channel, NotificationType type, string body) =>
        new(Guid.NewGuid(), recipientUserId, channel, type, body);

    public void MarkSent()
    {
        Status = NotificationStatus.Sent;
        SentAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed() => Status = NotificationStatus.Failed;
}
