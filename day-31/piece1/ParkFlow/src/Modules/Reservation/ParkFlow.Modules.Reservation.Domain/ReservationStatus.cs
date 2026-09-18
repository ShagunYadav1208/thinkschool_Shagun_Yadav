namespace ParkFlow.Modules.Reservation.Domain;

/// <summary>
/// Pending -> Confirmed -> CheckedIn -> Completed is the happy path.
/// Confirmed can also branch to Cancelled, NoShow, or Expired — see Reservation's transition
/// methods for exactly which moves are legal from which state.
/// </summary>
public enum ReservationStatus
{
    Pending = 0,
    Confirmed = 1,
    CheckedIn = 2,
    Completed = 3,
    Cancelled = 4,
    NoShow = 5,
    Expired = 6
}
