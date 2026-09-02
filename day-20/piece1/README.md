# Day 20 / Piece 1 — The outbox pattern

Backend is [day-19/piece1](../../day-19/piece1)'s `QuotesApi`, copied unmodified into
[QuotesApi/](QuotesApi/) so the outbox table + relay could be added without touching the read-only
original. Frontend is day-19/piece1's Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/) with one new tab: **Outbox**.

## Current status

**Deployed and verified live**, both frontend and backend, with a real, deterministic
crash-and-recover run - not simulated, not asserted.

- **Frontend:** https://black-desert-0fde3f100.7.azurestaticapps.net (day-17's Static Web App,
  redeployed with day-20's build - every prior day's tab is still there, plus Outbox).
- **API:** https://syquotes17-api.azurewebsites.net (day-17's App Service, redeployed with day-20's
  code, reusing day-19's Service Bus namespace - no new Azure resources for this day beyond a
  schema change on the existing database).
- **A deployment-only issue, not caught locally:** `Program.cs`'s Azure SQL path uses
  `EnsureCreatedAsync()`, which only creates a database's schema when the database doesn't exist
  yet - it does **not** reconcile schema changes into an already-existing database. Since
  `quotesdb` already existed (from day-17), deploying day-20's code did not add the new
  `OutboxMessages` table, and the first live `GET /api/outbox` returned a 500. This is precisely
  the risk day-17's own README flagged under "what would break this": *"a real schema change would
  need a proper SqlServer migrations history instead."* Fixed by creating the table directly
  against the live database (see verification-log.md section 9) rather than by changing the
  app's startup behavior - a real migrations history for the SQL Server path is the correct
  long-term fix and is out of scope for this session.

## Test isolation note (local testing, before deployment)

Day-19's App Service was still running its own live Service Bus consumers throughout local
development of this feature, which would have raced the crash-scenario tests below for the same
messages - see "Test isolation" in verification-log.md section 1 for why the App Service was
briefly stopped (with the user's approval) for that testing, then restarted before this feature was
deployed onto it.

## 1. The outbox table

```csharp
/// The outbox row: written in the SAME EF transaction as the domain change it describes (see
/// QuoteEndpointExtensions' POST handler), so the two can never diverge - either both commit, or
/// neither does. A separate relay (OutboxRelayService) polls for rows where ProcessedAt is still
/// null and publishes them; ProcessedAt is only set after a successful publish.
///
/// Id doubles as the Service Bus MessageId once the relay publishes it - stable across retries,
/// which is what lets the downstream consumer's ProcessedMessageTracker (day-19) recognize a
/// re-publish of the same row as a duplicate rather than a new event.
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // FK + navigation to Quote - the EF Core relationship this exercise asks for, configured
    // explicitly in QuotesDbContext.OnModelCreating rather than left to bare convention.
    public int QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;

    public required string EventType { get; set; }
    public required string Payload { get; set; }

    // DateTime (UTC), not DateTimeOffset - SQLite's EF provider can't translate ORDER BY on a
    // DateTimeOffset column (confirmed live - see verification-log.md).
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }

    public static OutboxMessage ForQuoteCreated(Quote quote) => new()
    {
        QuoteId = quote.Id,
        EventType = "QuoteCreated",
        Payload = JsonSerializer.Serialize(new OutboxQuotePayload(quote.Id, quote.Author, quote.Text, DateTimeOffset.UtcNow)),
    };
}

public record OutboxQuotePayload(int QuoteId, string Author, string Text, DateTimeOffset CreatedAt);
```

**The EF Core relationship** (this exercise's other named topic): `OutboxMessage.QuoteId` is a
required FK to `Quote`, configured explicitly rather than left to bare convention:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<OutboxMessage>()
        .HasOne(message => message.Quote)
        .WithMany(quote => quote.OutboxMessages)
        .HasForeignKey(message => message.QuoteId)
        .IsRequired()
        .OnDelete(DeleteBehavior.Cascade);
}
```

**Where it's written - the transaction** (`QuoteEndpointExtensions.cs`'s `POST /api/quotes/`):

```csharp
await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

var created = await repository.AddAsync(quote, cancellationToken);          // SaveChanges #1

var outboxMessage = OutboxMessage.ForQuoteCreated(created);
dbContext.OutboxMessages.Add(outboxMessage);
await dbContext.SaveChangesAsync(cancellationToken);                        // SaveChanges #2

await transaction.CommitAsync(cancellationToken);
```

`IQuoteRepository.AddAsync` already calls `SaveChangesAsync` internally - rather than change its
contract, the endpoint opens an explicit ambient transaction first, so that call and the outbox's
own `SaveChangesAsync` both participate in the *same* transaction instead of each getting its own
implicit one. Either both commit, or (on any exception before `CommitAsync`) neither does - the
`Quote` row and its `OutboxMessage` can never exist independently of each other.

## 2. The relay

```csharp
public class OutboxRelayService(
    IServiceScopeFactory scopeFactory,
    IQuoteEventPublisher publisher,
    ILogger<OutboxRelayService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessPendingAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox relay tick failed unexpectedly.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
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

        // Persisted BEFORE the publish - see section 3 for why this ordering matters.
        message.Attempts++;
        await db.SaveChangesAsync(cancellationToken);

        await publisher.PublishAsync(
            new QuoteCreatedEvent(message.Id, payload.QuoteId, payload.Author, payload.Text, payload.CreatedAt),
            cancellationToken);

        if (payload.Text.StartsWith("CRASH-RELAY:", StringComparison.Ordinal) && message.Attempts == 1)
        {
            logger.LogWarning("Simulating a relay crash after publishing {MessageId} but before marking it sent.", message.Id);
            throw new InvalidOperationException($"Simulated relay crash after publish, before commit, for {message.Id}.");
        }

        message.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
```

`GET /api/outbox` exposes every row (id, quote id, status, attempts, timestamps) for the UI and for
manual inspection; both it and the poll query above had to be fixed to sort by `CreatedAt` as
`DateTime`, not `DateTimeOffset` - see verification-log.md for the live SQLite error that caught
this.

## 3. The crash scenario tested, and why no message is lost or duplicated

**What was tested, live, twice - once via injected code, once via an uncontrolled real process
kill** (full transcripts and exact commands in verification-log.md):

- **A quote whose text carries the marker `CRASH-RELAY:`.** The relay publishes it successfully
  (confirmed independently - the message really did reach Service Bus and get consumed), then
  deliberately throws before it can set `ProcessedAt`. The row is left exactly as it would be after
  a real crash at that instant: `Attempts = 1`, `ProcessedAt = null`. On the relay's next poll (2s
  later), it finds the same row still unprocessed, increments `Attempts` to 2, and **publishes it
  again with the same message id**. This time nothing interrupts it, so `ProcessedAt` gets set and
  the row stops being retried.
- **A real `taskkill -9`-equivalent, no code injection.** Created a quote, hard-killed the whole
  API process within milliseconds (no graceful shutdown - the process was simply gone). Queried the
  SQLite file directly afterward (the process that would normally read it was dead) and confirmed
  the outbox row survived: `Attempts = 1, ProcessedAt = NULL`. Restarted the process; its relay
  found the same still-pending row on its first poll and completed it - again producing a real
  duplicate delivery (the original publish had, in fact, already reached Service Bus in the window
  before the kill), which the consumer deduped exactly the same way.

**Why no message is lost:** the outbox row is committed to the database in the *same* transaction
as the domain write, before either the API or the relay ever attempts to talk to Service Bus. A
crash at literally any point after that commit - mid-publish, right after a successful publish,
between ticks, or the whole process dying - leaves the row sitting durably in the database with
`ProcessedAt = null`. Nothing about "was this ever supposed to be sent" depends on any in-memory
state (unlike day-18's plain in-memory queue) or on the crashed process ever running again - any
future poll, by this same process after a restart or a different instance entirely, will find it
and try again.

**Why that can produce a duplicate, and why that's fine:** the relay cannot make "publish to
Service Bus" and "mark this row processed" happen atomically - they're two different systems, and
no distributed transaction spans them here. So the honest guarantee this pattern gives is
**at-least-once**, not exactly-once: a crash in the gap between those two steps means the same
message gets published again next tick. What makes that safe is entirely on the consumer side -
day-19's `ProcessedMessageTracker`, keyed on the message id (which is this row's own `Id`, stable
across every retry). A second delivery of the same id is recognized and its side effects skipped;
only the bookkeeping (`WasDuplicate = true`, still completing the message so it doesn't loop) runs
twice. **Screenshot proof:**
[screenshots/outbox-crash-test.png](screenshots/outbox-crash-test.png) - one outbox row, Attempts
column reading 2, two real deliveries listed against it, one explicitly marked a duplicate.

## What did I learn this session?

Two real bugs, both caught only by actually running the code, not by reading it:

1. Adding `Quote.OutboxMessages` as a navigation property - correct EF modeling for the
   relationship this exercise asks for - created a real `Quote -> OutboxMessage -> Quote -> ...`
   cycle the moment both sides got fixed up by the change tracker in the same request, and
   `System.Text.Json` doesn't detect that on its own; it just throws. `[JsonIgnore]` on the
   collection fixed it, but the lesson is that a "just add a navigation property" change to an
   entity that's ever returned directly from an endpoint isn't free.
2. SQLite's EF Core provider can't translate `ORDER BY` on a `DateTimeOffset` column at all - not a
   performance wrinkle, a hard `NotSupportedException` at query time. `DateTimeOffset` is the more
   "correct" type for a timestamp in general, but the moment EF needs to sort by it against SQLite,
   `DateTime` (UTC) is the pragmatic, portable choice.

Neither of these would have been caught by getting the transaction/relay logic itself right in
isolation - both only showed up once real data flowed through a real database.

## What would break this

- **The relay is a single poller in a single process.** If `QuotesApi` scales to multiple
  instances, every instance runs its own `OutboxRelayService`, all polling the same table - two
  instances could both pick up the same unprocessed row in the same window and both publish it
  (a second, independent source of duplicates beyond the crash scenario above, though still made
  safe by the same idempotent consumer). A production system would typically add row-level locking
  (`SELECT ... FOR UPDATE SKIP LOCKED` or equivalent) to let instances safely divide the work
  instead of racing over it.
- **The outbox table grows forever.** Nothing here archives or deletes processed rows. A real
  system needs a cleanup job for rows past some retention window.
- **A relay crash loop that isn't self-limiting.** The `CRASH-RELAY:` marker only fires once
  (guarded by `Attempts == 1`) specifically so this demo terminates - a *real* bug that crashes on
  every attempt (not a one-shot injection) would retry the same row forever, every 2 seconds,
  without ever being marked processed or failed. There's no dead-letter concept on the outbox side
  itself here (Service Bus's own DLQ, from day-19, only covers messages that make it that far).
- **The publisher itself throwing (not the relay crashing after a successful publish) is handled
  differently and not separately demonstrated here** - `PublishAsync` throwing would propagate out
  of `RelayOneAsync` before `Attempts` reflects a real send attempt at Service Bus, which is the
  correct behavior (nothing was actually sent) but wasn't exercised as its own scenario this
  session, only the "published successfully, then crashed" case was.

## GitHub link

Not pushed yet. Remote for the `thinkbridge-thinkschool` org is already configured locally as
`thinkschool` (per day-17's README); link to follow once pushed.

## Notes for mentor

- `day-19/piece1` (and everything upstream of it) was read-only reference / copy source - nothing
  there was modified. Any file needed from it was copied into `day-20/piece1` first.
- **Deployed live** onto day-17's existing App Service and Static Web App, same as day-19 (see
  "Current status" above). Before deployment, day-19's live App Service was briefly stopped (with
  the user's explicit approval) purely to eliminate a real competing-consumer race during local
  crash-scenario testing, then restarted - see verification-log.md section 1. After deployment, a
  separate, unrelated issue surfaced live: `EnsureCreatedAsync()` doesn't add new tables to an
  already-existing database, so the new `OutboxMessages` table had to be created directly against
  Azure SQL - see verification-log.md section 9 for the exact fix, including the temporary firewall
  rule opened and removed to run it.
- Full verification detail - both crash scenarios, the exact live output, and the two real bugs
  caught mid-session - is in [verification-log.md](verification-log.md).
