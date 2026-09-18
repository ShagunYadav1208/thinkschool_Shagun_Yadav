using ParkFlow.Modules.Notification.Application.Abstractions;
using ParkFlow.Modules.Notification.Domain;

namespace ParkFlow.Modules.Notification.Application.Notifications;

/// <summary>
/// Real wiring (see README's async flows) is a message broker consumer per integration event
/// (ReservationCreated, ReservationCancelled, ReservationExpired/no-show, PaymentCompleted) calling
/// the matching method below — those consumers aren't built in this piece, so the methods are
/// exposed directly for scaffolding.
/// </summary>
public sealed class NotificationApplicationService(
    INotificationRepository repository,
    IUnitOfWork unitOfWork,
    INotificationSender sender)
{
    public Task NotifyReservationConfirmedAsync(Guid recipientUserId, Guid reservationId, CancellationToken cancellationToken = default) =>
        SendAsync(recipientUserId, NotificationType.ReservationConfirmed,
            $"Your reservation {reservationId} is confirmed.", cancellationToken);

    public Task NotifyReservationCancelledAsync(Guid recipientUserId, Guid reservationId, CancellationToken cancellationToken = default) =>
        SendAsync(recipientUserId, NotificationType.ReservationCancelled,
            $"Your reservation {reservationId} was cancelled.", cancellationToken);

    public Task NotifyUpcomingReservationAsync(Guid recipientUserId, Guid reservationId, CancellationToken cancellationToken = default) =>
        SendAsync(recipientUserId, NotificationType.UpcomingReservation,
            $"Reminder: reservation {reservationId} starts soon.", cancellationToken);

    public Task NotifyNoShowAsync(Guid recipientUserId, Guid reservationId, CancellationToken cancellationToken = default) =>
        SendAsync(recipientUserId, NotificationType.NoShow,
            $"Reservation {reservationId} was marked as a no-show.", cancellationToken);

    public Task NotifyPaymentCompletedAsync(Guid recipientUserId, Guid paymentId, decimal amount, CancellationToken cancellationToken = default) =>
        SendAsync(recipientUserId, NotificationType.PaymentCompleted,
            $"Payment {paymentId} for {amount:C} was completed.", cancellationToken);

    private async Task SendAsync(Guid recipientUserId, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var message = NotificationMessage.Create(recipientUserId, NotificationChannel.Email, type, body);
        repository.Add(message);

        var delivered = await sender.SendAsync(message, cancellationToken);
        if (delivered)
        {
            message.MarkSent();
        }
        else
        {
            message.MarkFailed();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
