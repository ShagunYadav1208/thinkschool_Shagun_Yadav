namespace QuotesApi.ServiceBusMessaging;

public class AuditLogEntry
{
    public required string MessageId { get; init; }
    public required int QuoteId { get; init; }
    public required string Author { get; init; }
    public required string HandledBy { get; init; }
    public required bool WasDuplicate { get; init; }
    public DateTimeOffset HandledAt { get; init; } = DateTimeOffset.UtcNow;
}
