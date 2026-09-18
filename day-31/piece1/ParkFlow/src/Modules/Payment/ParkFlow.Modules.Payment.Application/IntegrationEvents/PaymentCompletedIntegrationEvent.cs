using ParkFlow.BuildingBlocks.Application;

namespace ParkFlow.Modules.Payment.Application.IntegrationEvents;

/// <summary>Flow 3: Notification reacts to this to tell the driver their charge went through.</summary>
public sealed record PaymentCompletedIntegrationEvent(Guid PaymentId, Guid ReservationId, decimal Amount) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
