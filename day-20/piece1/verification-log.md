# Verification log

## 1. Test isolation: why the production App Service was briefly stopped

Day 19's `QuotesApi` was deployed live onto `syquotes17-api` (see day-19/piece1/verification-log.md
section 8), and that deployment's own `AuditLogProcessorService`/`NotificationProcessorService`
have been running continuously ever since, consuming from the same real `audit-log`/`notifications`
Service Bus subscriptions this day's local testing also needs.

First local test run of a normal quote creation showed the outbox row reach `Sent` correctly, but
the local audit-log endpoint (`GET /api/servicebus/audit-log`) came back empty - the production
instance had consumed the message instead, confirmed by checking
`https://syquotes17-api.azurewebsites.net/api/servicebus/audit-log` directly and finding it there.
This is expected competing-consumer behavior (any consumer bound to a subscription can win a given
message), but it makes the crash-scenario tests below non-deterministic: if a duplicate delivery
from an injected crash landed on a *different* process than the original delivery, neither
process's own `ProcessedMessageTracker` would ever see both, and the "duplicate correctly detected"
proof would be un-provable, not because the mechanism failed but because of an artifact of testing
against shared infrastructure.

With the user's explicit approval (asked directly, since stopping a live service is a real,
if fully reversible, action):

```
az webapp stop -n syquotes17-api -g syquotes17-rg
```

All local testing in sections 2-4 below ran with the production instance stopped, then:

```
az webapp start -n syquotes17-api -g syquotes17-rg
```

Restart was not instant - the App Service's own container logs show a recurring
`ContainerStartupFailure` / exit-code-134 pattern that also appears in its history from *before*
today (08/29, 08/31), i.e. an intermittent B1-tier cold-start flakiness unrelated to anything this
session did. Polled with `until curl ... | grep -q "HTTP:200"; do sleep 5; done` rather than a fixed
sleep, since the exact recovery time wasn't predictable; confirmed fully back up and serving its
pre-existing data (`GET /api/quotes/` returned the same rows as before it was stopped) before
finishing this session.

## 2. Happy path - no crash

```
$ curl -X POST http://localhost:5922/api/quotes/ -d '{"author":"Seneca","text":"It is not that we have a short time to live, but that we waste a lot of it."}'
{"id":1,"author":"Seneca", ...}

$ curl http://localhost:5922/api/outbox
[{"id":"8097f33d-...","quoteId":1,"eventType":"QuoteCreated","createdAt":"...","processedAt":"...","attempts":1}]

$ curl http://localhost:5922/api/servicebus/audit-log
[{"messageId":"8097f33d-...","quoteId":1,"author":"Seneca","handledBy":"audit-worker-1","wasDuplicate":false, ...}]
```

One outbox row, `Attempts: 1`, `ProcessedAt` set, one consumer delivery, not a duplicate - the
un-crashed baseline every other scenario is compared against.

## 3. Crash scenario A - injected: relay publishes successfully, then is torn down before committing

```
$ curl -X POST http://localhost:5922/api/quotes/ -d '{"author":"Crash Test","text":"CRASH-RELAY: this event should be published twice but handled once"}'
{"id":2, ...}
```

Application log, same relay tick:

```
info: Executed DbCommand ... UPDATE "OutboxMessages" SET "Attempts" = @p0 WHERE "Id" = @p1
warn: QuotesApi.Outbox.OutboxRelayService[0]
      Simulating a relay crash after publishing outbox message 4f61e6e4-... but before marking it sent.
fail: QuotesApi.Outbox.OutboxRelayService[0]
      Outbox relay tick failed unexpectedly.
      System.InvalidOperationException: Simulated relay crash after publish, before commit, for outbox message 4f61e6e4-....
```

~2 seconds later (next poll), same row, no code changed - it just retried because `ProcessedAt` was
still null:

```
info: QuotesApi.Outbox.OutboxRelayService[0]
      Relayed outbox message 4f61e6e4-... for quote 2 (attempt 2).
```

Final state:

```
$ curl http://localhost:5922/api/outbox
[{"id":"4f61e6e4-...","quoteId":2,"attempts":2,"processedAt":"...", ...}, ...]

$ curl http://localhost:5922/api/servicebus/audit-log
[
  {"messageId":"4f61e6e4-...","handledBy":"audit-worker-1","wasDuplicate":true, "handledAt":"...24.39..."},
  {"messageId":"4f61e6e4-...","handledBy":"audit-worker-2","wasDuplicate":false,"handledAt":"...22.19..."},
  ...
]
```

Two real deliveries of the same message id, to two *different* competing workers, and the second
one correctly flagged `wasDuplicate: true`. The `notifications` subscription's independent consumer
showed the identical pattern in the same run.

## 4. Crash scenario B - uncontrolled: a real hard kill, no code injection

```
$ curl -X POST http://localhost:5922/api/quotes/ -d '{"author":"Kill Test","text":"created right before a hard process kill"}'
{"id":3, ...}
$ taskkill /PID <pid> /F
SUCCESS: The process with PID 32584 has been terminated.
```

The quote-creation request and the `taskkill` were issued back-to-back with no delay, well inside
the relay's 2-second poll interval - the intent was to kill the process before the relay could ever
attempt this row. Immediately after, with the process confirmed dead, queried the SQLite file
directly (nothing else was running that could have touched it):

```python
>>> sqlite3.connect('quotes.db').execute(
...   'SELECT Id, QuoteId, ProcessedAt, Attempts FROM OutboxMessages WHERE QuoteId = 3'
... ).fetchall()
[('3A9E9DE5-...', 3, None, 1)]
```

`Attempts = 1, ProcessedAt = NULL` - meaning the relay HAD reached this row (incremented and
persisted `Attempts` per the ordering described in README.md section 3) and, per the delivery
records after restart below, had also already called `PublishAsync` successfully, before the kill
landed. Restarted the process:

```
$ dotnet run --no-build --urls http://localhost:5922
info: QuotesApi.Outbox.OutboxRelayService[0]
      Outbox relay starting, polling every 00:00:02.
...
info: QuotesApi.Outbox.OutboxRelayService[0]
      Relayed outbox message 3a9e9de5-... for quote 3 (attempt 2).
```

```
$ curl http://localhost:5922/api/servicebus/audit-log
[
  {"messageId":"3a9e9de5-...","wasDuplicate":true, "handledAt":"...22.66..."},
  {"messageId":"3a9e9de5-...","wasDuplicate":false,"handledAt":"...22.03..."}
]
```

Both deliveries this time show timestamps *after* the restart - meaning the message really had
already reached Service Bus before the kill (in the same gap Scenario A demonstrates deliberately),
sat there undelivered while no consumer was running, and both the original and the retry's
duplicate were consumed once a consumer existed again. Nothing was lost across a real, uncontrolled
process death; the one duplicate produced was handled safely by the same idempotency mechanism.

## 5. Two real bugs, caught live

**Bug 1 - a JSON serialization cycle from a correctly-modeled EF relationship.** First `POST
/api/quotes/` after adding `Quote.OutboxMessages` returned an HTTP 500. Root cause: EF's change
tracker fixed up both navigation directions (`Quote.OutboxMessages` and the new row's
`OutboxMessage.Quote`) within the same request, and `Results.Created(..., created)` tried to
serialize the `Quote` - which now pointed to an `OutboxMessage` that pointed right back to the same
`Quote`. `System.Text.Json` doesn't detect reference cycles by default; it just recurses until it
gives up. Fixed with `[JsonIgnore]` on `Quote.OutboxMessages` - the API's public `Quote` shape was
never meant to expose outbox internals anyway.

**Bug 2 - SQLite can't `ORDER BY` a `DateTimeOffset` column.** Both the relay's poll query and
`GET /api/outbox` order by `CreatedAt`; with that column typed `DateTimeOffset`, every call threw:

```
System.NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset' in
ORDER BY clauses. Convert the values to a supported type, or use LINQ to Objects to order the
results on the client side.
```

`dotnet ef migrations has-pending-model-changes` reporting "no changes" while the running app threw
a `PendingModelChangesWarning` at startup was a brief red herring during the fix - resolved by a
clean `dotnet build` (a stale intermediate build artifact, not a real model/migration mismatch).
Fixed by changing `OutboxMessage.CreatedAt`/`ProcessedAt` to `DateTime` (UTC) and regenerating the
`AddOutboxMessages` migration - `DateTimeOffset` remains fine everywhere it's NOT an EF-mapped,
ordered column (`QuoteCreatedEvent`, and `OutboxQuotePayload` inside the opaque `Payload` JSON).

## 6. Graceful shutdown of the relay

Same verification technique as days 18-19 (a temporary `POST /api/test/shutdown` endpoint calling
`IHostApplicationLifetime.StopApplication()`, exercised, then removed):

```
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
info: QuotesApi.Outbox.OutboxRelayService[0]
      Outbox relay stopping.
info: QuotesApi.ServiceBusMessaging.NotificationProcessorService[0]
      Stopping notifications processor, letting any in-flight handler finish.
info: QuotesApi.ServiceBusMessaging.AuditLogProcessorService[0]
      Stopping audit-log processors, letting any in-flight handler finish.
info: QuotesApi.BackgroundJobs.QueuedHostedService[0]
      Graceful shutdown requested; giving the in-flight job (if any) time to unwind.
```

All four hosted services (day 18's queue, both day-19 Service Bus processors, and this day's relay)
stopped cleanly, no exceptions.

## 7. Backend build, frontend build/tests

```
dotnet build   -> Build succeeded, 0 errors (2 pre-existing NU1903 advisory warnings, unrelated)
npm install    -> 372 packages, clean
ng build       -> Application bundle generation complete, no errors
ng test        -> Test Files 4 passed (4), Tests 23 passed (23) - existing suite, untouched
```

## 8. Live in an actual browser

`ng serve` (isolated port, temporary proxy pointed at an isolated local test API instance),
production still stopped (section 1), driven with Playwright's Chromium:

- Created a normal quote via the **Outbox** tab's form. Screenshot:
  [screenshots/outbox-normal.png](screenshots/outbox-normal.png) - one row, `Sent`, `Attempts: 1`.
- Clicked **Run crash test**. Waited for the new row's "Deliveries seen downstream" column to show
  a duplicate. Final state, screenshot
  [screenshots/outbox-crash-test.png](screenshots/outbox-crash-test.png): four rows across two test
  runs, the crash-test row reading `Attempts: 2` with two delivery lines against it - one plain,
  one in orange reading "(duplicate, skipped)" - exactly matching the direct-API result in section 3.
- Console error listeners (`pageerror`/`console.error`): empty throughout.
- One retake was needed: the first attempt (before production was stopped) showed an empty
  "Deliveries seen downstream" column even for the normal quote - the production instance had
  consumed it instead, the same race described in section 1, caught here in the UI rather than via
  curl. Re-ran after stopping production; both rows in that second run show their deliveries
  correctly.

## 9. Deployment onto day-17's existing infrastructure, and the `EnsureCreatedAsync` gap

Same reused-infrastructure approach as day-19: `dotnet publish -c Release`, repacked as a
Unix-flagged zip (day-17's documented Kudu fix), `az webapp deploy --type zip` to `syquotes17-api`;
`ng build --configuration production`, deployed via the Static Web Apps CLI to `syquotes17-swa`.
Both deployments succeeded cleanly on the first attempt:

```
WARNING: Status: Site started successfully. Time: 64(s)
WARNING: Deployment has completed successfully
```

**Live smoke test surfaced a real bug the local SQLite-backed testing never could:**

```
$ curl https://syquotes17-api.azurewebsites.net/api/outbox
{"type":"...","title":"An unexpected error occurred.","status":500, ...}
```

`Program.cs`'s Azure SQL startup path calls `db.Database.EnsureCreatedAsync()` (a deliberate
simplification, already documented in day-17's README as needing "a proper SqlServer migrations
history instead" for a real schema change). `EnsureCreatedAsync` checks whether the database
exists at all - it does, `quotesdb` has held real quotes since day-17 - and if so does nothing
further; it never diffs the current model against the live schema, so the new `OutboxMessages`
table was simply never created. This is invisible locally because the local path uses
`MigrateAsync()` against a fresh SQLite file every time, which does apply new migrations correctly
- the two code paths have different failure modes, and only the one actually exercised against a
database that pre-dated this session's model changes could show it.

**Fix, applied directly against the live database** (not by changing app startup behavior, which
is a larger, separate decision about the SQL Server path's long-term migration story):

```sql
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OutboxMessages')
BEGIN
    CREATE TABLE [OutboxMessages] (
        [Id] uniqueidentifier NOT NULL,
        [QuoteId] int NOT NULL,
        [EventType] nvarchar(max) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ProcessedAt] datetime2 NULL,
        [Attempts] int NOT NULL,
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OutboxMessages_Quotes_QuoteId] FOREIGN KEY ([QuoteId]) REFERENCES [Quotes] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_OutboxMessages_QuoteId] ON [OutboxMessages] ([QuoteId]);
END
```

Run via a throwaway console app (`Microsoft.Data.SqlClient` + `Authentication=Active Directory
Default`, using the same signed-in `az` identity that's the SQL server's AAD admin from day-17's
setup) - `sqlcmd` wasn't available locally. Connecting required opening a temporary firewall rule
for the local machine's IP (`az sql server firewall-rule create`, since Azure SQL denies unlisted
client IPs by default); the rule was removed immediately after (`az sql server firewall-rule
delete`), confirmed by re-listing the server's firewall rules afterward - back to exactly the
pre-existing `AllowAzureServices` rule, nothing added left behind.

**Re-verified live after the fix:**

```
$ curl -X POST https://syquotes17-api.azurewebsites.net/api/quotes/ -d '{"author":"Marcus Aurelius","text":"Confine yourself to the present."}'
{"id":5, ...}

$ curl https://syquotes17-api.azurewebsites.net/api/outbox
[{"id":"82b153e1-...","quoteId":5,"processedAt":"...","attempts":1}]

$ curl https://syquotes17-api.azurewebsites.net/api/servicebus/audit-log
[{"messageId":"82b153e1-...","handledBy":"audit-worker-1","wasDuplicate":false, ...}]
```

Full pipeline confirmed live against real Azure SQL and real Service Bus: quote created, outbox row
written transactionally, relayed, consumed. Browser check (Playwright, real deployed URL): the
Outbox tab shows this same row, `Sent`, `Attempts: 1` - screenshot
[screenshots/live-outbox.png](screenshots/live-outbox.png). Console error listeners: empty.

## Side effects avoided

Every test run used an isolated API port and a temporary `ng serve --proxy-config`, never the
frontend's committed `proxy.conf.json`. `az webapp stop`/`start` targeted only `syquotes17-api`,
confirmed back to its pre-existing state (same quotes, same responsiveness) before finishing.
