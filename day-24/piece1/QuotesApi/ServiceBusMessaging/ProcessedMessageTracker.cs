using System.Collections.Concurrent;

namespace QuotesApi.ServiceBusMessaging;

/// <summary>
/// Per-subscription idempotency guard. Service Bus is at-least-once delivery - a message can be
/// redelivered (a lock expiring after processing succeeds but before CompleteMessageAsync's ack
/// round-trips back, a publisher retry sending the same MessageId again, etc.) - so every
/// subscriber tracks which MessageIds it has already handled and skips the real work on a repeat,
/// while still completing the message so it doesn't loop forever. One instance per subscription:
/// audit-log and notifications are independent consumers of the same topic and must dedupe
/// separately, not share a tracker.
/// </summary>
public class ProcessedMessageTracker
{
    private readonly ConcurrentDictionary<string, byte> _seen = new();

    /// <summary>True the first time this messageId is seen; false on every repeat.</summary>
    public bool TryMarkProcessed(string messageId) => _seen.TryAdd(messageId, 0);
}
