using ParkFlow.BuildingBlocks.Domain;
using ParkFlow.Modules.Payment.Domain.DomainEvents;

namespace ParkFlow.Modules.Payment.Domain;

/// <summary>
/// One charge for one reservation's stay. References the Reservation aggregate only by Id — this
/// module never loads or mutates a Reservation directly, only reacts to its integration events
/// (Flow 3 in the README).
/// </summary>
public sealed class Payment : AggregateRoot<Guid>
{
    public Guid ReservationId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private Payment()
    {
        // EF Core materialization.
    }

    private Payment(Guid id, Guid reservationId, decimal amount) : base(id)
    {
        ReservationId = reservationId;
        Amount = amount;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static Payment CreateFor(Guid reservationId, decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A payment amount cannot be negative.");
        }

        return new Payment(Guid.NewGuid(), reservationId, amount);
    }

    public void MarkCompleted()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"Payment {Id} cannot be completed while it is {Status}.");
        }

        Status = PaymentStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        Raise(new PaymentCompletedDomainEvent(Id, ReservationId, Amount));
    }

    public void MarkFailed()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"Payment {Id} cannot fail while it is {Status}.");
        }

        Status = PaymentStatus.Failed;
    }

    public void Refund()
    {
        if (Status != PaymentStatus.Completed)
        {
            throw new InvalidOperationException($"Payment {Id} cannot be refunded while it is {Status}.");
        }

        Status = PaymentStatus.Refunded;
    }
}
