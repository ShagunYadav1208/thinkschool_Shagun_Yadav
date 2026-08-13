# Day 4 - Serilog with correlation IDs

This piece reuses `QuotesIntegrationApi` from Day 3 Piece 5 (EF Core + SQLite, JWT auth) unmodified
in behavior, and replaces the default Microsoft.Extensions.Logging console output with Serilog:
structured properties instead of interpolated strings, a `TraceId` that ties every log line in a
request together, and per-category minimum levels set in `appsettings.json`.

## Run

```bash
dotnet run --project day-4/piece3/QuotesIntegrationApi
```

Runs in `Development` by default, which is what turns on the EF Core SQL Debug logging (see below).

## Serilog setup

**Packages** (`QuotesIntegrationApi.csproj`):

```xml
<PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
<PackageReference Include="Serilog.Settings.Configuration" Version="10.0.1" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
<!-- Wired up later, once a real Application Insights connection string exists. -->
<PackageReference Include="Serilog.Sinks.ApplicationInsights" Version="5.0.1" />
```

**Bootstrapping** (`Program.cs`, right after `WebApplication.CreateBuilder`):

```csharp
builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());
```

**Correlation middleware** (`Program.cs`, right after `app.Build()` and the migration block, before
`UseAuthentication`):

```csharp
app.Use((HttpContext ctx, RequestDelegate next) =>
{
    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
    {
        return next(ctx);
    }
});

app.UseSerilogRequestLogging();
```

`Enrich.FromLogContext()` (configured above) is what makes every logger — ours, EF Core's,
`Serilog.AspNetCore`'s own request-completion log — pick up whatever property is on the ambient
`LogContext` at the time it logs, so pushing `TraceId` once per request stamps every line written
while handling that request, not just our own.

**Structured call sites** (`Program.cs`, `POST /api/quotes`):

```csharp
logger.LogInformation("Received create-quote request from user {UserId} for author {Author}", userId, request.Author);
...
logger.LogInformation("Created quote {QuoteId} for user {UserId}", quote.Id, userId);
```

Never a `$"..."` interpolated string — `{QuoteId}` and `{UserId}` become indexed key-value pairs on
the log event, not baked into an opaque message string.

**Per-category levels** (`appsettings.json`):

```json
"Serilog": {
  "Using": ["Serilog.Sinks.Console"],
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning",
      "System": "Warning"
    }
  },
  "WriteTo": [
    { "Name": "Console", "Args": { "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {SourceContext}: {Message:lj}{NewLine}{Exception}" } }
  ],
  "Enrich": ["FromLogContext"]
}
```

and `appsettings.Development.json` layers one more override on top (only in dev):

```json
"Serilog": {
  "MinimumLevel": {
    "Override": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Debug"
    }
  }
}
```

Our own code (namespace `QuotesIntegrationApi`, category `Program`) falls under `Default: Information`
since nothing overrides it. `Microsoft.AspNetCore` is capped at `Warning` everywhere. EF Core's SQL
command text only shows up at `Debug`, and that override lives in `appsettings.Development.json`
alone — a Production deploy without that file inherits the base `Microsoft.EntityFrameworkCore:
Warning`, so raw SQL (and its parameter values) never reaches Production logs.

## 5+ correlated structured log lines from one request

Ran the app in `Development`, issued `POST /auth/token` then `POST /api/quotes` with the returned
bearer token. Every line below is one request's output, all sharing `TraceId`
`c688bd3e1fbc9cc7ff2a770cb6341451`:

```
[11:57:08 INF] [c688bd3e1fbc9cc7ff2a770cb6341451] Program: Received create-quote request from user user-42 for author Ada Lovelace
[11:57:08 DBG] [c688bd3e1fbc9cc7ff2a770cb6341451] Microsoft.EntityFrameworkCore.Database.Command: Executing DbCommand [Parameters=[@p0='?' (Size = 12), @p1='?' (DbType = DateTimeOffset), @p2='?' (Size = 56)], CommandType='Text', CommandTimeout='30']
INSERT INTO "Quotes" ("Author", "CreatedAt", "Text")
VALUES (@p0, @p1, @p2)
RETURNING "Id";
[11:57:08 INF] [c688bd3e1fbc9cc7ff2a770cb6341451] Microsoft.EntityFrameworkCore.Database.Command: Executed DbCommand (40ms) [Parameters=[@p0='?' (Size = 12), @p1='?' (DbType = DateTimeOffset), @p2='?' (Size = 56)], CommandType='Text', CommandTimeout='30']
INSERT INTO "Quotes" ("Author", "CreatedAt", "Text")
VALUES (@p0, @p1, @p2)
RETURNING "Id";
[11:57:08 INF] [c688bd3e1fbc9cc7ff2a770cb6341451] Program: Created quote 1 for user user-42
[11:57:08 INF] [c688bd3e1fbc9cc7ff2a770cb6341451] Serilog.AspNetCore.RequestLoggingMiddleware: HTTP POST /api/quotes responded 201 in 216.5246 ms
```

Note the earlier `POST /auth/token` request in the same run got its own, different TraceId
(`3204141051f8096d4ea9f53abbfdbbf0`) — proof the correlation is per-request, not per-process.

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-4/piece3

## Notes for mentor

Reused Day 3 Piece 5's `QuotesIntegrationApi` as-is and layered Serilog on top: `UseSerilog` +
`ReadFrom.Configuration` for setup, one `LogContext.PushProperty("TraceId", ...)` middleware for
correlation, and two structured `LogInformation` calls in the create-quote handler matching the
exercise's exact example line. `Serilog.Sinks.ApplicationInsights` is referenced but not wired into
`WriteTo` yet — no real instrumentation key exists for this exercise, and wiring a sink with a fake
key would either silently no-op or throw on startup, neither of which teaches anything. That's the
literal "(later)" the exercise names.

## What did I learn this session?

`Enrich.FromLogContext()` plus one `LogContext.PushProperty` call is the entire correlation
mechanism — it's not special middleware magic, it's an ambient (`AsyncLocal`-backed) property that
every enricher-aware log call picks up automatically, including ones we didn't write ourselves (EF
Core's SQL command logs, `Serilog.AspNetCore`'s own request-completion log). That's why the sample
above shows the same `TraceId` on an EF Core `Debug` line and our own `Information` line without
either of them knowing about the other.

## What would break this?

`ctx.TraceIdentifier` is only unique within a single process's lifetime by default (it's a per-request
counter-based ID, not a globally unique one) — across multiple instances behind a load balancer, two
different requests on two different machines could carry the same `TraceId`, which would break
correlation in a centralized log sink like Application Insights. The real fix is `Activity.Current?.Id`
(W3C trace-context, globally unique) instead of `HttpContext.TraceIdentifier` once this moves beyond
a single dev machine — which is exactly the OpenTelemetry piece this exercise flags as "later."
