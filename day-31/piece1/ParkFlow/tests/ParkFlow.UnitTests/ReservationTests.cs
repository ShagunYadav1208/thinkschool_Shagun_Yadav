using ParkFlow.Modules.Reservation.Domain;
using ParkFlow.Modules.Reservation.Domain.Exceptions;

namespace ParkFlow.UnitTests;

using Domain = ParkFlow.Modules.Reservation.Domain.Reservation;

public class ReservationTests
{
    private static Domain CreateReservation(DateTimeOffset? start = null) =>
        Domain.Create(
            userId: Guid.NewGuid(),
            vehicleId: Guid.NewGuid(),
            parkingSpotId: Guid.NewGuid(),
            startTime: start ?? DateTimeOffset.UtcNow.AddHours(1),
            endTime: (start ?? DateTimeOffset.UtcNow.AddHours(1)).AddHours(2),
            price: 25m,
            idempotencyKey: Guid.NewGuid());

    [Fact]
    public void Create_StartsInPendingStatus()
    {
        var reservation = CreateReservation();

        Assert.Equal(ReservationStatus.Pending, reservation.Status);
    }

    [Fact]
    public void Create_WithEndTimeBeforeStartTime_Throws()
    {
        var start = DateTimeOffset.UtcNow.AddHours(2);
        var end = start.AddHours(-1);

        Assert.Throws<ArgumentException>(() => Domain.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end, 10m, Guid.NewGuid()));
    }

    [Fact]
    public void HappyPath_PendingToConfirmedToCheckedInToCompleted_Succeeds()
    {
        var reservation = CreateReservation();

        reservation.Confirm();
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);

        reservation.CheckIn();
        Assert.Equal(ReservationStatus.CheckedIn, reservation.Status);

        reservation.Complete();
        Assert.Equal(ReservationStatus.Completed, reservation.Status);
    }

    [Fact]
    public void CheckIn_WithoutConfirming_Throws()
    {
        var reservation = CreateReservation();

        Assert.Throws<InvalidReservationStateTransitionException>(() => reservation.CheckIn());
    }

    [Fact]
    public void CheckIn_AfterCancellation_Throws()
    {
        var reservation = CreateReservation();
        reservation.Confirm();
        reservation.Cancel();

        Assert.Throws<InvalidReservationStateTransitionException>(() => reservation.CheckIn());
    }

    [Fact]
    public void Cancel_AfterCompleted_Throws()
    {
        var reservation = CreateReservation();
        reservation.Confirm();
        reservation.CheckIn();
        reservation.Complete();

        Assert.Throws<InvalidReservationStateTransitionException>(() => reservation.Cancel());
    }

    [Fact]
    public void MarkAsNoShow_BeforeCheckInDeadline_Throws()
    {
        var reservation = CreateReservation();
        reservation.Confirm();

        Assert.Throws<InvalidOperationException>(() => reservation.MarkAsNoShow(reservation.StartTime));
    }

    [Fact]
    public void MarkAsNoShow_AfterCheckInDeadline_Succeeds()
    {
        var reservation = CreateReservation();
        reservation.Confirm();

        reservation.MarkAsNoShow(reservation.CheckInDeadline.AddMinutes(1));

        Assert.Equal(ReservationStatus.NoShow, reservation.Status);
    }

    [Fact]
    public void Expire_FromPending_Succeeds()
    {
        var reservation = CreateReservation();

        reservation.Expire();

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
    }

    [Fact]
    public void Create_RaisesReservationCreatedDomainEvent()
    {
        var reservation = CreateReservation();

        var domainEvent = Assert.Single(reservation.DomainEvents);
        Assert.IsType<ParkFlow.Modules.Reservation.Domain.DomainEvents.ReservationCreatedDomainEvent>(domainEvent);
    }
}
