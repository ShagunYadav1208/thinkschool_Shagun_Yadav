using ParkFlow.BuildingBlocks.Domain;
using ParkFlow.Modules.Reservation.Domain.DomainEvents;
using ParkFlow.Modules.Reservation.Domain.Exceptions;

namespace ParkFlow.Modules.Reservation.Domain;

/// <summary>
/// The aggregate root for the whole system. A parking spot, a vehicle, and a time window only
/// become a real booking through here — no other module is allowed to flip a reservation's status
/// directly, which is why every transition below is a method, not a settable property.
///
/// Grace window: a driver has <see cref="CheckInGracePeriod"/> after <see cref="StartTime"/> to
/// check in before the reservation becomes eligible for no-show (rule 6).
/// </summary>
public sealed class Reservation : AggregateRoot<Guid>
{
    public static readonly TimeSpan CheckInGracePeriod = TimeSpan.FromMinutes(30);

    public Guid UserId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid ParkingSpotId { get; private set; }
    public DateTimeOffset StartTime { get; private set; }
    public DateTimeOffset EndTime { get; private set; }
    public decimal Price { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid IdempotencyKey { get; private set; }

    public DateTimeOffset CheckInDeadline => StartTime + CheckInGracePeriod;

    private Reservation()
    {
        // EF Core materialization.
    }

    private Reservation(
        Guid id,
        Guid userId,
        Guid vehicleId,
        Guid parkingSpotId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        decimal price,
        Guid idempotencyKey,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        VehicleId = vehicleId;
        ParkingSpotId = parkingSpotId;
        StartTime = startTime;
        EndTime = endTime;
        Price = price;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
        Status = ReservationStatus.Pending;
    }

    /// <summary>
    /// Creates a new reservation in the Pending state (rule 3: start/end must be a valid window).
    /// Rule 7 (idempotency key uniqueness) and rule 1 (no overlapping active reservations for the
    /// same spot) are enforced by the Application layer + a database constraint, not here — the
    /// aggregate alone cannot see other reservations.
    /// </summary>
    public static Reservation Create(
        Guid userId,
        Guid vehicleId,
        Guid parkingSpotId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        decimal price,
        Guid idempotencyKey)
    {
        if (startTime >= endTime)
        {
            throw new ArgumentException("A reservation's start time must be before its end time.");
        }

        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");
        }

        var reservation = new Reservation(
            Guid.NewGuid(), userId, vehicleId, parkingSpotId, startTime, endTime, price,
            idempotencyKey, DateTimeOffset.UtcNow);

        reservation.Raise(new ReservationCreatedDomainEvent(
            reservation.Id, userId, vehicleId, parkingSpotId, startTime, endTime, price, idempotencyKey));

        return reservation;
    }

    public void Confirm()
    {
        EnsureStatusIs(ReservationStatus.Pending, nameof(Confirm));
        Status = ReservationStatus.Confirmed;
    }

    /// <summary>
    /// Rule 4: cannot check in once cancelled/expired (or completed/no-show — those are also not
    /// Confirmed, so the single status guard below covers all of them).
    /// </summary>
    public void CheckIn()
    {
        EnsureStatusIs(ReservationStatus.Confirmed, nameof(CheckIn));
        Status = ReservationStatus.CheckedIn;
    }

    public void Complete()
    {
        EnsureStatusIs(ReservationStatus.CheckedIn, nameof(Complete));
        Status = ReservationStatus.Completed;
        Raise(new ReservationCompletedDomainEvent(Id, ParkingSpotId, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Rule 5: a completed reservation can never be cancelled — only Pending/Confirmed can be.
    /// </summary>
    public void Cancel()
    {
        if (Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
        {
            throw new InvalidReservationStateTransitionException(Status, nameof(Cancel));
        }

        Status = ReservationStatus.Cancelled;
        Raise(new ReservationCancelledDomainEvent(Id, ParkingSpotId));
    }

    /// <summary>
    /// Rule 6: only legal once the check-in grace window has actually passed.
    /// </summary>
    public void MarkAsNoShow(DateTimeOffset now)
    {
        EnsureStatusIs(ReservationStatus.Confirmed, nameof(MarkAsNoShow));

        if (now < CheckInDeadline)
        {
            throw new InvalidOperationException(
                $"Reservation {Id} cannot be marked as a no-show before its check-in deadline of {CheckInDeadline:O}.");
        }

        Status = ReservationStatus.NoShow;
        Raise(new ReservationNoShowDomainEvent(Id, ParkingSpotId));
    }

    /// <summary>
    /// Used by the background expiration worker (Flow 2) for reservations nobody ever confirmed or
    /// acted on in time.
    /// </summary>
    public void Expire()
    {
        if (Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
        {
            throw new InvalidReservationStateTransitionException(Status, nameof(Expire));
        }

        Status = ReservationStatus.Expired;
        Raise(new ReservationExpiredDomainEvent(Id, ParkingSpotId));
    }

    private void EnsureStatusIs(ReservationStatus required, string attemptedTransition)
    {
        if (Status != required)
        {
            throw new InvalidReservationStateTransitionException(Status, attemptedTransition);
        }
    }
}
