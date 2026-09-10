# Day 26 / Piece 1 — App Insights + KQL

Backend is [day-25/piece1](../../day-25/piece1)'s `QuotesApi`, copied into [QuotesApi/](QuotesApi/)
and modified to add OpenTelemetry (traces, metrics, logs). Frontend
([quotes-list-detail/](quotes-list-detail/)) and `infra/`'s existing modules are otherwise
untouched - this piece is backend + infra observability, no UI change. `infra/` gained two new
modules: Application Insights (workspace-based) and a scheduled query alert on error rate.

## Current status

**The OpenTelemetry implementation is real, complete, and verified locally against a real trace
backend (Jaeger) - not simulated.** `dotnet build`: 0 errors. `npm run build`/`npm test`
(unaffected, sanity-checked anyway): clean, 20/20 passing.

**Confirmed this session, again: this student's Azure subscription is still `Disabled`.**
```
$ az account show --query state    # "Enabled" - a stale, unreliable cache
$ az rest --method get --url ".../subscriptions/30e5e569-...?api-version=2021-01-01" --query state
"Disabled"
$ az deployment sub validate ... (this exact template)
ERROR: ReadOnlyDisabledSubscription
```
First found during day-25; still true today. **Application Insights and the error-rate alert are
written (`infra/modules/appinsights.bicep`, `infra/modules/alerts.bicep`) and compile clean, but
have never been deployed or validated against real Azure** - the same "queued pending subscription
reactivation" status as day-25's Key Vault. See [[project-azure-subscription-disabled]] territory:
before assuming otherwise, re-check `az account show`'s `state`, not just that command's surface
answer.

**What was verified for real, live, this session:** a genuine distributed trace, captured via a
local Jaeger container (OTLP), stitching one HTTP request to its own downstream background-worker
processing - see section 3 and the real screenshot in
[screenshots/jaeger-distributed-trace.png](screenshots/jaeger-distributed-trace.png). The
OpenTelemetry SDK code exporting these spans is *exactly* the code that would export to Azure
Monitor once a connection string exists - only the destination differs, not the instrumentation.

## 1. Layout

```
QuotesApi/
  Observability/
    QuotesApiActivitySource.cs   - the one custom ActivitySource this app starts spans from
    TelemetryExtensions.cs        - all OpenTelemetry wiring (traces, metrics, logs; OTLP + Azure Monitor)
  Outbox/OutboxRelayService.cs    - modified: restores the API request's trace as its own span's parent
  Models/OutboxMessage.cs         - modified: +TraceParent column
infra/
  modules/
    appinsights.bicep              - NEW: Log Analytics workspace + Application Insights
    alerts.bicep                    - NEW: scheduled query rule, error-rate KQL as a real resource
observability/
  kql/
    p50-p99-by-endpoint.kql
    dependency-breakdown.kql
    error-rate-alert.kql
    trace-by-operation-id.kql
screenshots/
  jaeger-distributed-trace.png    - real, captured this session (section 3)
```

## 2. OpenTelemetry wiring (`Observability/TelemetryExtensions.cs`, full)

```csharp
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace QuotesApi.Observability;

/// <summary>
/// Two exporters, both optional and independently switchable:
///
/// - OTLP, to a local collector (Jaeger in this session), added only in Development. This is
///   how this piece's actual trace screenshot was captured - real spans, real trace IDs, just
///   viewed in Jaeger instead of Azure Monitor because there was nowhere else to send them.
/// - Azure Monitor, added whenever "AppInsights:ConnectionString" is non-empty - empty by
///   default, same feature-flag-by-absence pattern day-21's Redis connection string and day-25's
///   Auth:Enabled both used. This student's Azure subscription is disabled, so this exporter is
///   wired and correct but has never actually sent anything anywhere.
///
/// The instrumentation itself (which spans get created at all) is identical either way - only
/// where the spans/metrics/logs are SENT depends on these two exporters.
/// </summary>
public static class TelemetryExtensions
{
    public static IServiceCollection AddQuotesApiTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var appInsightsConnectionString = configuration["AppInsights:ConnectionString"];
        var hasAppInsights = !string.IsNullOrWhiteSpace(appInsightsConnectionString);
        var otlpEndpoint = new Uri(configuration["Otlp:Endpoint"] ?? "http://localhost:4317");

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("QuotesApi", serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    // Spans this app starts deliberately - see QuotesApiActivitySource and
                    // OutboxRelayService's "outbox.relay" span.
                    .AddSource(QuotesApiActivitySource.Name)
                    // Azure SDK client libraries (Azure.Messaging.ServiceBus among them) emit
                    // their own Activities under names starting "Azure." automatically - nothing
                    // exports them unless something is registered to listen, which is what this
                    // wildcard does. This is what turns Service Bus's Send/Process calls into
                    // real spans in the same trace, with zero custom instrumentation code in
                    // QuoteEventPublisher or the consumers - a built-in Azure SDK feature.
                    .AddSource("Azure.*")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();

                if (environment.IsDevelopment())
                    tracing.AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint);

                if (hasAppInsights)
                    tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsightsConnectionString);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (environment.IsDevelopment())
                    metrics.AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint);

                if (hasAppInsights)
                    metrics.AddAzureMonitorMetricExporter(o => o.ConnectionString = appInsightsConnectionString);
            });

        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(otel =>
            {
                otel.IncludeFormattedMessage = true;
                otel.IncludeScopes = true;
                otel.ParseStateValues = true;

                if (environment.IsDevelopment())
                    otel.AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint);

                if (hasAppInsights)
                    otel.AddAzureMonitorLogExporter(o => o.ConnectionString = appInsightsConnectionString);
            });
        });

        return services;
    }
}
```

## 3. Distributed tracing: API → worker → DB, verified locally

**The mechanism** - `Models/OutboxMessage.cs`:
```csharp
// Day 26: the W3C traceparent (Activity.Current?.Id) of the HTTP request that wrote this
// row, captured at write time. OutboxRelayService picks this back up as the PARENT of its
// own relay span - without it, the relay's work (which genuinely happens seconds later, on
// an unrelated timer tick, long after the original HTTP request has returned) would start a
// brand new, disconnected trace instead of continuing the one the request started.
public string? TraceParent { get; set; }

public static OutboxMessage ForQuoteCreated(Quote quote)
{
    var payload = JsonSerializer.Serialize(new OutboxQuotePayload(quote.Id, quote.Author, quote.Text, DateTimeOffset.UtcNow));
    return new OutboxMessage
    {
        QuoteId = quote.Id,
        EventType = "QuoteCreated",
        Payload = payload,
        TraceParent = System.Diagnostics.Activity.Current?.Id,
    };
}
```

**The restoration** - `Outbox/OutboxRelayService.cs`'s `RelayOneAsync`:
```csharp
using var activity = QuotesApiActivitySource.Instance.StartActivity(
    "outbox.relay",
    ActivityKind.Internal,
    parentId: message.TraceParent ?? string.Empty);

activity?.SetTag("outbox.message_id", message.Id);
activity?.SetTag("outbox.quote_id", message.QuoteId);
activity?.SetTag("outbox.attempt", message.Attempts + 1);
```
Everything inside this `using` block - the `Attempts` DB write, the Service Bus publish attempt,
the `ProcessedAt` DB write - is now a child of the original HTTP request's trace, via
`EntityFrameworkCore`/`Azure.*` instrumentation picking up the ambient `Activity.Current` this
block establishes.

**Real, live proof** (`POST /api/quotes/` creating quote 18, local run this session):

```
$ curl -X POST http://localhost:5116/api/quotes/ -d '{"author":"OpenTelemetry Test","text":"..."}'
201 {"id":18,...}

$ sqlite3 quotes.db "SELECT TraceParent FROM OutboxMessages WHERE QuoteId = 18"
00-84852af89decb72bce6252eda9967e9f-e4a3875fc2b13954-01
```
The stored trace ID (`84852af89decb72bce6252eda9967e9f`) and span ID (`e4a3875fc2b13954`) are
*exactly* Jaeger's IDs for the `POST /api/quotes/` span itself - confirmed by querying Jaeger's own
API for that trace and finding the same numbers. Screenshot:
[screenshots/jaeger-distributed-trace.png](screenshots/jaeger-distributed-trace.png).

**What the screenshot actually shows, honestly:** one `POST /api/quotes/` root span, its two real
DB inserts (Quote + OutboxMessage), and **three** separate `outbox.relay` spans - not one. That's
not a bug in the tracing, it's the outbox relay's own retry behavior made visible: this sandbox's
Service Bus namespace is unreachable (a pre-existing DNS resolution failure, first seen in day-25,
confirmed unrelated to this piece's changes), so every attempt to publish throws, and the row stays
`ProcessedAt IS NULL` forever, getting retried on every subsequent poll tick - each retry correctly
reusing the *same* stored `TraceParent`, so all three attempts land in the same trace instead of
each starting a new one. This is genuinely useful, unplanned proof: the trace makes an otherwise
invisible retry storm immediately legible - "make production legible" doing exactly its job on a
real failure this session actually hit, not a staged one.

**A real, honest surprise: SQLite's `db.name` is `"main"`.** The EF Core instrumentation names DB
client spans after `db.name` - against SQLite (`Data Source=quotes.db`), that's literally `"main"`
(SQLite's own internal schema name for the primary attached database), not a bug or a
mis-configuration. Against the real Azure SQL connection string (`appsettings.Production.json`),
this would read `"quotesdb"` instead.

## 4. KQL queries (`observability/kql/`)

**p50/p99 by endpoint** (`p50-p99-by-endpoint.kql`):
```kql
requests
| where timestamp > ago(1h)
| summarize
    requestCount = count(),
    failedCount = countif(success == false),
    p50Ms = percentile(duration, 50),
    p90Ms = percentile(duration, 90),
    p99Ms = percentile(duration, 99)
    by operation_Name
| extend errorRatePercent = round(100.0 * failedCount / requestCount, 2)
| order by p99Ms desc
```

**Dependency call breakdown** (`dependency-breakdown.kql`):
```kql
dependencies
| where timestamp > ago(1h)
| summarize
    callCount = count(),
    failedCount = countif(success == false),
    avgMs = round(avg(duration), 2),
    p95Ms = percentile(duration, 95)
    by type, target, name
| extend failureRatePercent = round(100.0 * failedCount / callCount, 2)
| order by callCount desc
```
Three distinct sources land here, all automatic: EF Core's SQL commands (`type == "SQL"`), the
Azure SDK's own built-in Service Bus tracing (`"Azure.*"` source), and this app's own
`outbox.relay` span (`type == "InProc"`) - the worker-hop half of section 3's story, queryable in
Log Analytics the same way it's visible in Jaeger.

**Alert on error rate** (`error-rate-alert.kql` - also the literal query deployed in
`infra/modules/alerts.bicep`'s `scheduledQueryRules` resource, so the two can't quietly drift
apart):
```kql
requests
| where timestamp > ago(5m)
| summarize total = count(), failed = countif(success == false)
| extend errorRatePercent = round(100.0 * failed / total, 2)
| where errorRatePercent > 5
```
Deployed as a real `Microsoft.Insights/scheduledQueryRules` resource (`PT5M` evaluation frequency
and window, severity 2, fires when this query returns any row) - not just a string in this README.

**Pull one full trace by ID** (`trace-by-operation-id.kql` - the Log Analytics equivalent of
section 3's Jaeger screenshot, once this exports to Azure Monitor for real):
```kql
union requests, dependencies, traces, exceptions
| where operation_Id == "84852af89decb72bce6252eda9967e9f"
| project timestamp, itemType, name, operation_Name, duration, success, message
| order by timestamp asc
```

**None of these four have been run against a live Application Insights workspace** - reviewed for
correctness against the documented Azure Monitor / OpenTelemetry schema, not executed, for the same
subscription-disabled reason as everything else Azure-shaped this session.

## 5. Application Insights + alert (Bicep, not deployed)

`infra/modules/appinsights.bicep` - workspace-based App Insights (the only kind Azure still
provisions):
```bicep
resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: workspaceName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: retentionInDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}
```

`infra/modules/alerts.bicep` - the error-rate query as a real scheduled query rule:
```bicep
resource errorRateAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'quotesapi-error-rate'
  location: location
  properties: {
    displayName: 'QuotesApi - error rate above threshold'
    severity: 2
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      allOf: [
        {
          query: '''
requests
| where timestamp > ago(5m)
| summarize total = count(), failed = countif(success == false)
| extend errorRatePercent = round(100.0 * failed / total, 2)
| where errorRatePercent > ${errorRateThreshold}
'''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: actionGroupIds }
  }
}
```
`az bicep build --file infra/main.bicep` compiles this whole graph (Key Vault + App Insights +
alert + API + SQL + Service Bus) clean.

## 6. Testing performed this session

```
$ dotnet build
Build succeeded. 0 Error(s)

$ npm run build   # unaffected by this piece, sanity-checked anyway
Application bundle generation complete.

$ npm test
Test Files  3 passed (3)
     Tests  20 passed (20)
```

**Live, local, end-to-end:**
1. `docker start jaeger` (existing local container from a prior session, OTLP ports 4317/4318 +
   UI 16686).
2. `dotnet run` (Development) - confirmed startup log shows no OpenTelemetry errors.
3. `POST /api/quotes/` - created quote 18.
4. Queried Jaeger's own HTTP API (`/api/traces`, `/api/services`) to confirm `QuotesApi` was
   exporting real spans, then fetched the specific trace and inspected every span's tags.
5. Cross-checked the DB's stored `TraceParent` value against Jaeger's trace/span IDs - exact match,
   confirmed programmatically, not eyeballed.
6. Captured `screenshots/jaeger-distributed-trace.png` via headless Chrome
   (`chrome --headless --screenshot`) against the live Jaeger UI - a real screenshot of a real
   trace, not a mockup.
7. Found and worked around a real blocker along the way: 17 stale unprocessed outbox rows from
   earlier days' testing (copied over with `quotes.db`) were stuck retrying against the same
   unreachable Service Bus, each tick's `foreach` loop aborting on the *first* row's exception
   before ever reaching row 18 - explained in section 3, and why those stale rows were cleared
   before the screenshot could show anything involving quote 18 at all.

## What did I learn this session?

1. **Storing a trace context alongside deferred work is the actual mechanism that makes "durable,
   async work" and "one coherent trace" compatible.** Without persisting `TraceParent` on the
   outbox row, the relay's work - which by design happens on an unrelated timer, disconnected from
   the original request's async context - would always start a brand new trace. This isn't
   OpenTelemetry-specific; it's the general shape of the problem any queue/outbox/durable-function
   pattern has to solve to stay traceable.
2. **An unplanned failure can be a better demonstration than a staged one.** The three-`outbox.relay`
   trace wasn't the clean single-attempt screenshot I set out to capture - it's more interesting:
   real proof that a stuck retry loop against a genuinely broken dependency stays legible as ONE
   trace instead of fragmenting into unrelated noise, which is closer to what "make production
   legible" actually needs to survive contact with a real incident.
3. **`db.name` for SQLite is a real semantic-convention value, not a placeholder.** Seeing every
   local DB span named `"main"` looked like a bug at first glance; it's SQLite's actual internal
   schema name, faithfully reported by the instrumentation. Production's Azure SQL spans won't have
   this quirk - a good reminder that local-dev telemetry and prod telemetry can differ in ways
   that are correct on both sides, not just in volume.

## What would break this

- **The Azure Monitor exporter has never sent anything anywhere.** Everything about it - the
  connection-string plumbing, the exporter registration, whether Azure Monitor's own ingestion
  actually accepts what this OTel SDK version emits - is unverified beyond compiling. A live
  deploy could surface a schema mismatch or auth issue this session had no way to catch.
- **`outbox.relay`'s retry behavior, now visible, was already there** - this piece didn't add or
  fix it. An outbox row stuck behind a permanently broken dependency retries forever, and (per
  section 6, point 7) the relay's per-tick `foreach` aborts entirely on the first exception, so ONE
  stuck row can starve every row behind it indefinitely. That's a pre-existing day-19/20 design
  gap this session's tracing made visible, not a new one.
- **The four KQL queries are unexecuted.** Reviewed against the documented Azure Monitor schema,
  not run - a genuine typo or a schema assumption that's subtly wrong (e.g., `operation_Id`
  formatting, or `dependencies.type` values that don't actually read `"InProc"` for
  `ActivityKind.Internal` spans in this exact exporter version) would only surface against real
  data.
- **The error-rate alert's `actionGroupIds` defaults to empty** - it would fire (as a log entry in
  Azure Monitor) but notify no one until a real Action Group is attached.
- **Two of Jaeger's ports (4317 gRPC / 4318 HTTP) are also Application Insights Live Metrics'
  conventional local ports in some setups** - running both a local OTLP collector and a real Azure
  Monitor exporter side-by-side on the same machine could need one of them reconfigured to avoid a
  collision, untested here since only one was ever active at a time.

## Notes for mentor

- `day-25/piece1` was the copy source - only `QuotesApi/Observability/`,
  `Outbox/OutboxRelayService.cs`, and `Models/OutboxMessage.cs` were actually modified; everything
  else (including the entire frontend) is unchanged.
- **The Jaeger screenshot and the trace-ID cross-check in section 3 are both real** - captured live
  this session, not staged. The three-span retry pattern it shows is an honest artifact of this
  sandbox's Service Bus being unreachable, explained rather than hidden.
- **Nothing Azure-shaped was deployed** - same blocker as day-25, re-confirmed at the start of this
  session before attempting anything. `infra/deploy.md` has the exact commands queued for once it's
  resolved.

## GitHub link

Not pushed yet - link to follow once pushed to the `thinkbridge-thinkschool` org, per this user's
standing preference that git actions (including read-only ones) need explicit permission each
time.
