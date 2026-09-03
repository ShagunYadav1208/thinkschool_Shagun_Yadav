namespace QuotesApi.ServiceBusMessaging;

public class NotificationEntry
{
    public required string MessageId { get; init; }
    public required int QuoteId { get; init; }
    public required string Message { get; init; }
    public required bool WasDuplicate { get; init; }
    public DateTimeOffset HandledAt { get; init; } = DateTimeOffset.UtcNow;
}
