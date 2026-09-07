using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.ServiceBusMessaging;

namespace QuotesApi.Outbox;

/// <summary>
/// Polls the outbox table for rows where ProcessedAt is still null, publishes each one to Service
/// Bus, and only then marks it processed. This is the "relay" half of the transactional outbox
/// pattern: the API request that wrote the outbox row (QuoteEndpointExtensions) never talks to
/// Service Bus itself, so a Service Bus outage - or the API process dying the instant after its
/// transaction commits - can never lose the message. It's durably sitting in the database, and this
/// loop (running here, or after a full restart, or on a different instance entirely) will find it
/// on its next poll.
///
/// What this pattern does NOT give you is exactly-once delivery - see the crash-injection block in
/// RelayOneAsync and README.md for the deliberate demonstration of why, and why that's fine as long
/// as the consumer is idempotent (day-19's ProcessedMessageTracker, keyed on this row's own Id).
/// </summary>
public class OutboxRelayService(
    IServiceScopeFactory scopeFactory,
    IQuoteEventPublisher publisher,
    ILogger<OutboxRelayService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox relay starting, polling every {Interval}.", PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A tick-level failure (e.g. the DB itself is briefly unreachable) must not kill
                // the relay loop - there's always a next poll, and nothing was lost either way,
                // since nothing pending was ever marked processed.
                logger.LogError(ex, "Outbox relay tick failed unexpectedly.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Outbox relay stopping.");
    }

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var message in pending)
            await RelayOneAsync(db, message, cancellationToken);
    }

    private async Task RelayOneAsync(QuotesDbContext db, OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<OutboxQuotePayload>(message.Payload)!;

        // Attempts is persisted BEFORE the publish call, not after - deliberately, so the crash
        // injected below (which happens after a REAL publish but before this row is ever marked
        // processed) still leaves a durable trace that this was a second attempt, not a first. If
        // the count were only saved on success, a crash here would erase all memory of the attempt
        // and this demo would throw forever instead of exactly twice.
        message.Attempts++;
        await db.SaveChangesAsync(cancellationToken);

        await publisher.PublishAsync(
            new QuoteCreatedEvent(message.Id, payload.QuoteId, payload.Author, payload.Text, payload.CreatedAt),
            cancellationToken);

        // Crash-injection for the exercise's required demo: a quote whose text carries this marker
        // publishes successfully (Service Bus really did receive it - confirmed independently via
        // the audit-log/notifications endpoints) but the relay is torn down before it can record
        // that fact. On the NEXT tick, this same row is still ProcessedAt == null, so it gets
        // published AGAIN with the SAME message id - a real duplicate delivery, which is exactly
        // what "at-least-once" means and exactly what the downstream ProcessedMessageTracker
        // (day-19) exists to make safe. Attempts == 1 guards it to fire exactly once: attempt 2
        // proceeds to actually commit, so the loop terminates instead of crashing forever.
        if (payload.Text.StartsWith("CRASH-RELAY:", StringComparison.Ordinal) && message.Attempts == 1)
        {
            logger.LogWarning(
                "Simulating a relay crash after publishing outbox message {MessageId} but before marking it sent.",
                message.Id);
            throw new InvalidOperationException($"Simulated relay crash after publish, before commit, for outbox message {message.Id}.");
        }

        message.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Relayed outbox message {MessageId} for quote {QuoteId} (attempt {Attempts}).",
            message.Id,
            message.QuoteId,
            message.Attempts);
    }
}
