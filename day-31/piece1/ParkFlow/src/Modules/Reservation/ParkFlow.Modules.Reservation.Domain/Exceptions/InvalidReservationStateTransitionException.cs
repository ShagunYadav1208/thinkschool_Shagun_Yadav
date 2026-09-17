namespace ParkFlow.Modules.Reservation.Domain.Exceptions;

public sealed class InvalidReservationStateTransitionException(
    ReservationStatus currentStatus,
    string attemptedTransition)
    : InvalidOperationException(
        $"Cannot {attemptedTransition} a reservation that is currently {currentStatus}.")
{
    public ReservationStatus CurrentStatus { get; } = currentStatus;
}
