using System.Text.Json;

namespace QuotesApi.Models;

/// <summary>
/// The outbox row: written in the SAME EF transaction as the domain change it describes (see
/// QuoteEndpointExtensions' POST handler), so the two can never diverge - either both commit, or
/// neither does. A separate relay (OutboxRelayService) polls for rows where ProcessedAt is still
/// null and publishes them; ProcessedAt is only set after a successful publish.
///
/// Id doubles as the Service Bus MessageId once the relay publishes it - stable across retries,
/// which is what lets the downstream consumer's ProcessedMessageTracker (day-19) recognize a
/// re-publish of the same row as a duplicate rather than a new event.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // FK + navigation to Quote - the EF Core relationship this exercise asks for, configured
    // explicitly in QuotesDbContext.OnModelCreating rather than left to bare convention.
    public int QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;

    public required string EventType { get; set; }
    public required string Payload { get; set; }

    // DateTime (UTC), not DateTimeOffset - confirmed live that EF Core's SQLite provider can't
    // translate ORDER BY on a DateTimeOffset column ("SQLite does not support expressions of type
    // 'DateTimeOffset' in ORDER BY clauses"), which both OutboxRelayService's poll query and
    // GET /api/outbox need. DateTimeOffset is fine everywhere it's NOT an EF-mapped, ORDER-BY'd
    // column - QuoteCreatedEvent and OutboxQuotePayload (opaque JSON inside Payload, below) keep it.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }

    public static OutboxMessage ForQuoteCreated(Quote quote)
    {
        var payload = JsonSerializer.Serialize(new OutboxQuotePayload(quote.Id, quote.Author, quote.Text, DateTimeOffset.UtcNow));

        return new OutboxMessage
        {
            QuoteId = quote.Id,
            EventType = "QuoteCreated",
            Payload = payload,
        };
    }
}

public record OutboxQuotePayload(int QuoteId, string Author, string Text, DateTimeOffset CreatedAt);
