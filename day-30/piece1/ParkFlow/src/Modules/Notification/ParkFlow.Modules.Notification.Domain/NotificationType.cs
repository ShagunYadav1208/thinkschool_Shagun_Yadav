namespace ParkFlow.Modules.Notification.Domain;

/// <summary>What the notification is about — matches the four notification-triggering flows in the README.</summary>
public enum NotificationType
{
    ReservationConfirmed = 0,
    ReservationCancelled = 1,
    UpcomingReservation = 2,
    NoShow = 3,
    PaymentCompleted = 4
}
