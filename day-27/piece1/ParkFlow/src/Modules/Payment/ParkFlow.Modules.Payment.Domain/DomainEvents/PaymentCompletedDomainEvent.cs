using ParkFlow.BuildingBlocks.Domain;

namespace ParkFlow.Modules.Payment.Domain.DomainEvents;

public sealed record PaymentCompletedDomainEvent(Guid PaymentId, Guid ReservationId, decimal Amount) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
