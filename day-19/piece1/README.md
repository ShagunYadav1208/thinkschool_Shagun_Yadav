# Day 19 / Piece 1 — Azure Service Bus topics + DLQ

Backend is [day-18/piece1](../../day-18/piece1)'s `QuotesApi`, copied unmodified into
[QuotesApi/](QuotesApi/) so Service Bus publishing/consuming could be added without touching the
read-only original. Frontend is day-18/piece1's Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/) with one new tab: **Service Bus**.

## Current status

**Deployed and verified live**, not just tested locally.

- **Frontend:** https://black-desert-0fde3f100.7.azurestaticapps.net (day-17's Static Web App,
  redeployed with day-19's build - every prior day's tab is still there, plus Background Jobs and
  Service Bus).
- **API:** https://syquotes17-api.azurewebsites.net (day-17's App Service, redeployed with day-19's
  code).
- **Service Bus namespace:** `syquotes19sb.servicebus.windows.net` (new resource group
  `syquotes19-rg`, `eastasia`, under the `Azure for Students` subscription) - real, Standard tier,
  not a local emulator or an in-process simulation. Chosen deliberately: Basic tier doesn't support
  topics/subscriptions at all, and this exercise specifically needs a topic with two subscriptions
  and a real DLQ.
- **This has an ongoing cost** - Standard tier has a small fixed monthly base fee (no free topic
  tier exists). Confirmed with the user before provisioning (see brief-to-agent.md). Worth deleting
  `syquotes19-rg` once this is reviewed, if it isn't going to be reused for a later day.
- Full publish -> both subscriptions -> idempotent dedupe -> poison message -> DLQ pipeline
  exercised live end-to-end, in a real browser, against **both** a local test instance and the
  actual deployed site. Screenshots in [screenshots/](screenshots/); full transcript in
  [verification-log.md](verification-log.md).
- **The existing App Service and Static Web App were reused, not duplicated** - both were already
  running (and already costing money) from day-17, and day-19's code is a strict superset of
  day-17's (every endpoint and tab from day-17 and day-18 is still present) - so redeploying onto
  them is a genuine upgrade, not a loss of anything. The App Service's existing managed identity
  (already used for Azure SQL) was additionally granted access to the new Service Bus namespace -
  same zero-secret pattern, one more resource.

## 1. What was built, and why it's architected this way

- **No secrets anywhere** - same "zero secrets in config" architecture day-17 used for Azure SQL
  via Managed Identity. `appsettings.json` holds only the namespace's fully-qualified DNS name (not
  a secret) and entity names. Locally, `AzureCliCredential` picks up the already-logged-in `az`
  session; in a real deployment, `DefaultAzureCredential` would pick up an App Service's managed
  identity. See section "A real bug caught" below for why these are two different code paths
  instead of one.
- **One topic, two subscriptions** - `quote-events` topic; `audit-log` (max delivery count 5, two
  competing consumers) and `notifications` (max delivery count 3, 15s lock, the poison/DLQ demo).
  `RequiresDuplicateDetection` is left off on the topic on purpose - the idempotency this exercise
  asks for is a subscriber-side concern (a message redelivered to the *same* consumer), not a
  publisher-side one (Service Bus silently dropping a second publish), and turning on topic-level
  duplicate detection would have made it impossible to demonstrate the subscriber-side dedupe
  cleanly (the second publish would never arrive at all).
- **[QuotesApi/ServiceBusMessaging/](QuotesApi/ServiceBusMessaging/)** - the publisher
  (`QuoteEventPublisher`), the two competing-consumer/DLQ-demo `BackgroundService`s
  (`AuditLogProcessorService`, `NotificationProcessorService`), the idempotency guard
  (`ProcessedMessageTracker`), the in-memory logs the UI reads (`EventLogStore<T>`), and the DLQ
  peek (`DeadLetterInspector`).
- **[QuotesApi/Extensions/ServiceBusEndpointExtensions.cs](QuotesApi/Extensions/ServiceBusEndpointExtensions.cs)** -
  `GET /api/servicebus/audit-log`, `GET /api/servicebus/notifications`, `GET /api/servicebus/dlq`,
  `POST /api/servicebus/replay/{quoteId}` (the idempotency demo), `POST /api/servicebus/poison` (the
  DLQ demo). `POST /api/quotes/` (existing endpoint) now also publishes a `QuoteCreated` event,
  best-effort - a Service Bus outage is logged, not allowed to fail a quote creation that already
  succeeded against the database.
- **[quotes-list-detail/src/app/service-bus-view/](quotes-list-detail/src/app/service-bus-view/)** -
  new tab: create-a-quote form (publishes), "Replay last event" and "Send poison message" buttons,
  and three polling tables (audit log, notifications, DLQ).

**Screenshots** (from the live browser run in verification-log.md):

- [screenshots/service-bus-after-publish.png](screenshots/service-bus-after-publish.png)
- [screenshots/service-bus-after-replay.png](screenshots/service-bus-after-replay.png) - audit log
  and notifications both show two rows for the same message id: one `No`, one `Yes (skipped)`.
- [screenshots/service-bus-dlq.png](screenshots/service-bus-dlq.png) - two poison messages, both
  `MaxDeliveryCountExceeded` after 3 deliveries.

## 2. Publisher

```csharp
public class QuoteEventPublisher : IQuoteEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public QuoteEventPublisher(ServiceBusClient client, ServiceBusOptions options)
    {
        _sender = client.CreateSender(options.TopicName);
    }

    public async Task PublishAsync(QuoteCreatedEvent quoteEvent, CancellationToken cancellationToken)
    {
        var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(quoteEvent))
        {
            MessageId = quoteEvent.EventId.ToString(),
            ContentType = "application/json",
            Subject = "QuoteCreated",
        };

        await _sender.SendMessageAsync(message, cancellationToken);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
```

`MessageId` is set explicitly to the event's own `EventId` (a `Guid` generated by the caller), not
left for Service Bus to assign - that's what lets `POST /api/servicebus/replay/{quoteId}` re-send
the *exact same* message id later and prove subscriber-side dedupe rather than relying on Service
Bus's own (disabled here) duplicate detection.

## 3. Consumer - competing consumers, and the idempotency key

```csharp
public class AuditLogProcessorService(
    ServiceBusClient client,
    ServiceBusOptions options,
    IEventLogStore<AuditLogEntry> store,
    ILogger<AuditLogProcessorService> logger) : BackgroundService
{
    private const int WorkerCount = 2;
    private readonly ProcessedMessageTracker _tracker = new();
    private readonly List<ServiceBusProcessor> _processors = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var i = 1; i <= WorkerCount; i++)
        {
            var workerId = $"audit-worker-{i}";
            var processor = client.CreateProcessor(
                options.TopicName,
                options.AuditLogSubscription,
                new ServiceBusProcessorOptions { MaxConcurrentCalls = 1, AutoCompleteMessages = false });

            processor.ProcessMessageAsync += args => HandleMessageAsync(args, workerId);
            processor.ProcessErrorAsync += args =>
            {
                logger.LogError(args.Exception, "{Worker} error on {EntityPath}", workerId, args.EntityPath);
                return Task.CompletedTask;
            };

            _processors.Add(processor);
            await processor.StartProcessingAsync(stoppingToken);
        }

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args, string workerId)
    {
        var quoteEvent = JsonSerializer.Deserialize<QuoteCreatedEvent>(args.Message.Body)!;
        var isFirstDelivery = _tracker.TryMarkProcessed(args.Message.MessageId);

        store.Add(new AuditLogEntry
        {
            MessageId = args.Message.MessageId,
            QuoteId = quoteEvent.QuoteId,
            Author = quoteEvent.Author,
            HandledBy = workerId,
            WasDuplicate = !isFirstDelivery,
        });

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var processor in _processors) await processor.StopProcessingAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
        foreach (var processor in _processors) await processor.DisposeAsync();
    }
}
```

**Two `ServiceBusProcessor`s, one subscription, on purpose** - this is the competing-consumer proof:
both `audit-worker-1` and `audit-worker-2` pull from the same `audit-log` subscription, so a burst
of messages splits across them rather than each worker seeing everything. Confirmed live: the
original publish landed on `audit-worker-2`; the later replay of the *same message id* landed on
`audit-worker-1` - a different worker instance - and still correctly recognized the duplicate,
because `ProcessedMessageTracker` is one shared instance for the whole service, not one per worker.

**The idempotency key is `args.Message.MessageId`** - the same `EventId` the publisher set. Every
handler calls `ProcessedMessageTracker.TryMarkProcessed(messageId)`:

```csharp
public class ProcessedMessageTracker
{
    private readonly ConcurrentDictionary<string, byte> _seen = new();

    /// <summary>True the first time this messageId is seen; false on every repeat.</summary>
    public bool TryMarkProcessed(string messageId) => _seen.TryAdd(messageId, 0);
}
```

On a repeat, the handler still completes the message (so it doesn't loop forever) but skips the
real side effect and records `WasDuplicate = true` instead - visible directly in the audit log and
notifications tables.

## 4. Proof a poison message landed in the DLQ

`NotificationProcessorService`'s handler throws, deliberately and unconditionally, for any event
whose text starts with `POISON:`:

```csharp
private async Task HandleMessageAsync(ProcessMessageEventArgs args)
{
    var quoteEvent = JsonSerializer.Deserialize<QuoteCreatedEvent>(args.Message.Body)!;

    if (quoteEvent.Text.StartsWith("POISON:", StringComparison.Ordinal))
    {
        logger.LogWarning(
            "Poison event {MessageId} (delivery attempt {Count}) - throwing deliberately.",
            args.Message.MessageId, args.Message.DeliveryCount);

        throw new InvalidOperationException($"Simulated permanent failure processing event {args.Message.MessageId}.");
    }
    // ...idempotent happy path, same shape as AuditLogProcessorService...
}
```

The exception is left uncaught deliberately. `ServiceBusProcessor` abandons a message automatically
when the handler throws (incrementing its delivery count) regardless of `AutoCompleteMessages` -
that setting only governs the successful-return path. The `notifications` subscription's
`MaxDeliveryCount` is 3; once exceeded, Service Bus itself dead-letters the message with reason
`MaxDeliveryCountExceeded` - no application code calls `DeadLetterMessageAsync` explicitly.

**Live proof**, via `GET /api/servicebus/dlq` (peeks the subscription's dead-letter sub-queue
directly, independent of anything the app itself logged):

```
$ curl -X POST http://localhost:5921/api/servicebus/poison
{"eventId":"481c4fd6-...","quoteId":-1,"author":"Poison Test","text":"POISON: ...", ...}

# ~45s later (3 attempts x 15s lock duration)
$ curl http://localhost:5921/api/servicebus/dlq
[{
  "messageId": "481c4fd6-...",
  "deliveryCount": 3,
  "deadLetterReason": "MaxDeliveryCountExceeded",
  "deadLetterErrorDescription": "Message could not be consumed after 3 delivery attempts.",
  ...
}]
```

Application log for the same run, showing the three attempts as they happened:

```
warn: Poison event 481c4fd6-... (delivery attempt 1) - throwing deliberately.
warn: Poison event 481c4fd6-... (delivery attempt 2) - throwing deliberately.
warn: Poison event 481c4fd6-... (delivery attempt 3) - throwing deliberately.
```

Full transcript, including the browser-driven repeat of this same proof, in
[verification-log.md](verification-log.md).

## 5. Deployment - and a real bug only deployment caught

Deployed onto day-17's existing infrastructure rather than provisioning new App Service/Static Web
App resources (see "Current status" above for why): granted the App Service's managed identity
`Azure Service Bus Data Owner` on `syquotes19sb`, added the `ServiceBus` section to
`appsettings.Production.json`, `dotnet publish -c Release`, repacked the output as a Unix-flagged
zip (day-17's verification-log.md documents why - Kudu's Linux zip extractor mangles Windows-flagged
entries), `az webapp deploy --type zip`. Frontend: `ng build --configuration production`, deployed
via the Static Web Apps CLI (`swa deploy ... --deployment-token ... --env production`) using the
namespace's existing deployment token.

**The bug:** first deployment attempt looked fine (build succeeded, site loaded, quotes still
worked) but both new tabs silently showed "no events"/"no jobs" with zero visible error. Root cause,
found by testing the actual live site in a browser rather than trusting the deploy logs: both
`jobs.service.ts` (Day 18) and `service-bus.service.ts` (this day) hardcoded a *relative*
`/api/...` base URL, copied from the existing `QuotesService` pattern without noticing that
`QuotesService`'s `apiBaseUrl` is relative **only in the dev environment file** - `environment.prod.ts`
overrides it to the App Service's absolute URL specifically because the deployed frontend runs as
static files with no dev-server proxy behind it. Neither `JobsService` nor `ServiceBusService` had
an equivalent prod override; on the live site, their relative paths resolved against the *Static Web
App's own origin*, silently hit its SPA navigation fallback, and got back `index.html` (a valid
`200 text/html` response) instead of JSON - which `HttpClient` fails to parse, and which this app's
own polling code swallows silently by design (so a transient network hiccup during a 2-second poll
doesn't flash an error banner). The combination - a real request, a real 200, an invisible failure
mode - is exactly why "code review + unit tests" isn't the same claim as "deployed and verified,"
which is the same lesson day-17 drew from its own two platform bugs.

**Fix:** added `apiOrigin` to both environment files (`''` in dev, the real App Service URL in
prod) and rebuilt both services' base URLs as `` `${environment.apiOrigin}/api/...` ``. Rebuilt,
redeployed, re-verified live in a browser - both tabs now show real data on the deployed site (see
verification-log.md).

## What did I learn this session?

`DefaultAzureCredential`'s fallback chain doesn't behave the way its name suggests in every
environment. Confirmed live: outside of an actual Azure compute resource, `ManagedIdentityCredential`
fails with a hard `AuthenticationFailedException` after several IMDS probe timeouts, and
`DefaultAzureCredential` does **not** fall through to `AzureCliCredential` after that in this SDK
version - it just throws. The fix (day-17 already established the pattern, this session just
re-learned it in a new spot) is the same either way: pick the credential explicitly by environment
rather than trust the chain to sort it out, so local dev is fast and predictable and production
still gets managed identity.

The deployment bug (section 5) taught the same kind of lesson from the other direction: an
environment-specific override (`environment.prod.ts`) that exists for one service's URL doesn't
automatically apply to a new service someone adds later by copying an existing pattern - the
override has to be copied too, deliberately, or the new code silently inherits the wrong
environment's assumption. Both lessons this session are versions of "don't trust the default path
to be right everywhere without checking" - once for a credential chain, once for a URL.

## What would break this

- **Standard tier is real money, ongoing.** Unlike SQLite or an in-memory queue, this doesn't stop
  costing anything when the terminal is closed. Worth deleting `syquotes19-rg` once this is
  reviewed and no longer needed.
- **`ProcessedMessageTracker` is in-memory and per-process.** Scaling `QuotesApi` to two instances
  gives each its own tracker - a message redelivered to instance A after instance B already handled
  it would be treated as new. A production system would need a shared dedupe store (a database
  table keyed on message id, or Service Bus's own duplicate detection at the *publish* side for
  publisher retries specifically, which is a different scenario than what's demonstrated here).
- **`EventLogStore<T>` and the DLQ peek are unbounded/unpaginated.** `GetAll()` returns everything
  ever seen; `PeekMessagesAsync` caps at 50. Fine for a demo, not for a namespace that's been
  running for weeks.
- **The poison marker (`Text.StartsWith("POISON:")`) is a string convention, not a real failure
  mode.** A real poison message is usually malformed JSON, a schema version the consumer doesn't
  understand, or a downstream dependency that's actually down - the mechanics demonstrated here
  (uncaught exception -> auto-abandon -> delivery count -> DLQ) are identical either way, but a real
  system needs a real trigger, not a string prefix.
- **Two competing workers in the same process only proves the pattern, not real horizontal
  scaling.** Running two actual separate instances of `QuotesApi` (two processes, or two
  App Service instances) against the same subscription would demonstrate the same competing-consumer
  behavior across process boundaries, which is the scenario this pattern is really for.
- **Redeploying onto day-17's shared App Service/SWA means there's no longer a separately-viewable
  "day-17 as it was."** The live URLs now serve day-19's superset build. Nothing from day-17's
  functionality was removed (every endpoint and tab still works), but anyone wanting to see
  day-17's code in isolation needs to read `day-17/piece1/` rather than visit the live URL.

## GitHub link

Not pushed yet. Remote for the `thinkbridge-thinkschool` org is already configured locally as
`thinkschool` (per day-17's README); link to follow once pushed.

## Notes for mentor

- `day-18/piece1` (and everything upstream of it) was read-only reference / copy source - nothing
  there was modified. Any file needed from it was copied into `day-19/piece1` first.
- **Real Azure resources were provisioned for this exercise** (resource group `syquotes19-rg`,
  Service Bus namespace `syquotes19sb`, Standard tier) - confirmed with the user before creating
  anything, given the ongoing cost and that Basic tier can't do topics/subscriptions at all. Full
  provisioning commands and the RBAC grant (`Azure Service Bus Data Owner`, scoped to just this
  namespace, for local `az`-session auth - zero connection strings/keys anywhere) are in
  verification-log.md section 1.
- **Deployed live** onto day-17's existing App Service and Static Web App (see "Current status" and
  section 5 above) - both were already running, so this is a redeploy of newer code onto existing
  paid infrastructure, not new billable resources for compute/hosting. Only the Service Bus
  namespace is new spend.
- Full verification detail, including the live shutdown log for both new `BackgroundService`s, the
  credential-chain bug, and the deployment-only `apiOrigin` bug, is in
  [verification-log.md](verification-log.md).
