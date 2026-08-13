# Day 4 - OpenTelemetry tracing

This piece reuses `QuotesIntegrationApi` from Day 4 Piece 3 (EF Core + SQLite, JWT auth, Serilog
with a `TraceId` correlation middleware) unmodified in behavior, and adds OpenTelemetry tracing on
top: every request now generates a trace with nested spans for its EF Core query, and the `TraceId`
Serilog was already emitting is switched to be the *actual* OTel trace ID instead of ASP.NET Core's
own `HttpContext.TraceIdentifier` — so logs and traces now correlate for real, not just by naming
coincidence.

## Run

Requires a local OTLP collector — this was verified against a real local Jaeger:

```bash
docker run -d --name jaeger \
  -p 16686:16686 -p 4317:4317 -p 4318:4318 \
  -e COLLECTOR_OTLP_ENABLED=true \
  jaegertracing/all-in-one:latest

dotnet run --project day-4/piece4/QuotesIntegrationApi
```

Jaeger UI: http://localhost:16686 — search for service `QuotesIntegrationApi`.

## OTel setup

**Packages** (`QuotesIntegrationApi.csproj`):

```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.17.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.17.0" />
<!-- Still beta upstream — EF Core's DiagnosticSource events aren't stable enough yet for the
     OTel team to ship 1.0, but it's the standard choice (65M+ downloads) for this. -->
<PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.17.0-beta.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.17.0" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.17.0" />
```

**Configuration** (`Program.cs`, right after the Serilog setup):

```csharp
const string ServiceName = "QuotesIntegrationApi";

// One ActivitySource for spans we start ourselves.
builder.Services.AddSingleton(new ActivitySource(ServiceName));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddSource(ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(
                builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        }));
```

`AddHttpClientInstrumentation()` is wired but has nothing to instrument yet — this API makes no
outbound HTTP calls of its own. It's there so the first `HttpClient` call added later shows up in
traces automatically, with no further setup.

**Making Serilog's `TraceId` the real OTel trace ID** (`Program.cs`, the correlation middleware):

```csharp
app.Use((HttpContext ctx, RequestDelegate next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
    using (LogContext.PushProperty("TraceId", traceId))
    {
        return next(ctx);
    }
});
```

Piece 3 used `ctx.TraceIdentifier` here — a per-process ASP.NET Core request counter, not a real
trace ID. `AddAspNetCoreInstrumentation()` starts a span for every incoming request *before* this
middleware runs, so `Activity.Current` is already that span by the time this line executes.
Reading `Activity.Current.TraceId` here means the value Serilog stamps on every log line is exactly
the trace ID the OTLP exporter sends to Jaeger.

**Custom span for logic the automatic instrumentations don't see** (`Program.cs`, `POST /api/quotes`):

```csharp
using (var activity = activitySource.StartActivity("validate-create-quote-request"))
{
    activity?.SetTag("user.id", userId);
    // ... validation ...
    activity?.SetTag("validation.error_count", errors.Count);
    if (errors.Count > 0)
        activity?.SetStatus(ActivityStatusCode.Error, "Validation failed");
}
```

Request-body validation is plain in-process logic — no EF call, no HTTP call — so neither
`AddAspNetCoreInstrumentation()` nor `AddEntityFrameworkCoreInstrumentation()` produces a span for
it on their own. Starting one explicitly here nests it under the request span automatically
(`ActivitySource.StartActivity` parents to whatever `Activity.Current` already is).

## Verified trace: nested spans for a real request

Ran the app against the local Jaeger above, got a token, then `POST /api/quotes`. Queried Jaeger's
trace API directly (`GET /api/traces/{traceId}`) rather than only eyeballing the UI:

```
Trace ID: d96adb2eca7f4005ca96298efdcbf61b
Span count: 3

- 'POST /api/quotes/'              (span=dade306f166d6b78, parent=ROOT,           duration=217249us)
  - 'validate-create-quote-request' (span=e2eda462ab3a6759, parent=dade306f166d6b78, duration=102us)
    tags: user.id=user-77, validation.error_count=0
  - 'main' [EF Core INSERT]         (span=74d34ec374e9780a, parent=dade306f166d6b78, duration=22044us)
    tags: db.system=sqlite, db.statement=INSERT INTO "Quotes" ("Author","CreatedAt","Text") VALUES (@p0,@p1,@p2) RETURNING "Id";

Service: QuotesIntegrationApi (telemetry.sdk.name=opentelemetry, telemetry.sdk.language=dotnet, sdk 1.17.0)
```

That's `AddAspNetCoreInstrumentation()`'s root span, our custom `validate-create-quote-request`
span, and `AddEntityFrameworkCoreInstrumentation()`'s span for the actual `INSERT` — all three
correctly nested under one trace, with real SQL text and our custom tags attached.

Cross-checking against the app's console log for the same request confirms the correlation claim
literally, not just in theory — same 32-hex-char ID on every line:

```
[12:30:55 INF] [d96adb2eca7f4005ca96298efdcbf61b] Program: Received create-quote request from user user-77 for author Grace Hopper
[12:30:55 INF] [d96adb2eca7f4005ca96298efdcbf61b] Program: Created quote 1 for user user-77
[12:30:55 INF] [d96adb2eca7f4005ca96298efdcbf61b] Serilog.AspNetCore.RequestLoggingMiddleware: HTTP POST /api/quotes responded 201 in 212.8232 ms
```

**On the screenshot**: I don't have a browser available in this environment to capture the Jaeger
UI directly, so I verified the trace via Jaeger's HTTP API instead (above) — that's the same data
the UI renders, just as JSON rather than a picture. The Jaeger container from the `docker run`
command above is still running; open http://localhost:16686, select service `QuotesIntegrationApi`,
and the same trace (`d96adb2eca7f4005ca96298efdcbf61b`) — or a fresh one from a new request — will
render as a proper waterfall with the three nested spans above, ready to screenshot.

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-4/piece4

## Notes for mentor

Built directly on Day 4 Piece 3 rather than starting fresh from Day 3, since this exercise's own
premise ("the OTel TraceId is the same one Serilog emits") only means something if there's an
existing Serilog setup to correlate with. The one behavior change to Piece 3's code is the
correlation middleware switching from `ctx.TraceIdentifier` to `Activity.Current.TraceId` — flagged
in Piece 3's own README as the fix "later" would bring; this is that later.

## What did I learn this session?

`ActivitySource.StartActivity` doesn't need to be told what its parent is — it just picks up
whatever `Activity.Current` already is at the moment it's called. That's the entire mechanism
behind nested spans across completely decoupled code (our middleware, our handler, EF Core's
internals, the ASP.NET Core hosting layer) all ending up correctly parented in the same trace
without any of them passing a trace context object to each other explicitly.

## What would break this?

`OpenTelemetry.Instrumentation.EntityFrameworkCore` is still beta (`1.17.0-beta.1`) and has been for
years — if EF Core changes its internal `DiagnosticSource` event shape in a future version before
this instrumentation stabilizes, EF spans could silently stop appearing (or appear with missing
tags) with no compile-time warning, since the coupling is via untyped diagnostic events, not a
typed API. Also: `AddOtlpExporter()` silently drops spans if the collector endpoint is unreachable
(by design — a tracing failure shouldn't take down the app) which is correct for production
resilience, but means a wrong `OpenTelemetry:OtlpEndpoint` value in config would produce zero
visible errors and just a permanently empty trace list in Jaeger — worth an explicit health check
if this ever mattered for an SLA.
