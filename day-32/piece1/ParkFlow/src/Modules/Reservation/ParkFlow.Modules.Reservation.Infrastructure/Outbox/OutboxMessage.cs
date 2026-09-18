namespace ParkFlow.Modules.Reservation.Infrastructure.Outbox;

/// <summary>
/// Written in the same transaction as the aggregate change (see Flow 1), then drained by a
/// separate publisher process/worker and pushed onto the message broker. This is what makes "the
/// reservation was saved" and "the event will eventually be published" an atomic guarantee instead
/// of two operations that can fail independently.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
    public DateTimeOffset? ProcessedOn { get; set; }
}
