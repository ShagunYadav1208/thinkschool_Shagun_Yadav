# Day 5 - Diagnose a slow endpoint using your traces

This piece builds on [Day 4 Piece 5](../../day-4/piece5) (Serilog + OpenTelemetry + local Jaeger
export + Azure Monitor). It adds one new endpoint with a deliberate performance bug, uses the
existing tracing to find it, then fixes it and proves the fix in a second trace.

## What's real here, and what isn't

This environment has no display/browser, so I could not take a literal Jaeger UI screenshot. What
I could do, and did, instead:

- Started a real Jaeger container locally (`docker run jaegertracing/all-in-one`), ran the actual
  app against it with the real OTLP exporter from `Program.cs`, seeded 300 real quotes across 6
  authors through the real HTTP API, and hit the buggy and fixed endpoints for real.
- Pulled the resulting traces back out of **Jaeger's own HTTP API**
  (`http://localhost:16686/api/traces/...`) — the same data the UI renders, not a mock — and saved
  them as [`evidence/trace-before-summary.json`](evidence/trace-before-summary.json) and
  [`evidence/trace-after-summary.json`](evidence/trace-after-summary.json). Each includes the real
  trace ID, span counts, durations, and the exact `db.statement` SQL text Jaeger recorded.
- Anyone with Docker can reproduce this exactly and see it in the UI: `docker run -d --name jaeger
  -p 16686:16686 -p 4317:4317 jaegertracing/all-in-one`, run the app, open
  `http://localhost:16686`, search service `QuotesIntegrationApi`, and paste in either trace ID
  above (`029aae18e16903973f96864818935f5d` before, `20309a4408db1ac5eb910d5c35eb550b` after) — the
  waterfall view will show exactly what the JSON below describes.

## The bug: `GET /api/quotes/author-stats`

A new endpoint that, for every quote, also reports how many quotes that quote's author has
written in total — a reasonable feature, implemented the way it's easy to write by accident:

```csharp
var allQuotes = await db.Quotes.AsNoTracking().OrderBy(q => q.Id).ToListAsync(cancellationToken);

var results = new List<object>(allQuotes.Count);
foreach (var quote in allQuotes)
{
    var authorQuoteCount = await db.Quotes
        .AsNoTracking()
        .CountAsync(q => q.Author == quote.Author, cancellationToken);   // <- one query per row

    results.Add(new { quote.Id, quote.Author, quote.Text, AuthorQuoteCount = authorQuoteCount });
}
```

One query to list the quotes, then one more `SELECT COUNT(*)` per row — the classic N+1. With 300
seeded quotes that's 301 database round trips for a single HTTP request.

## Diagnosis note (the exercise deliverable)

> This trace showed the slow span was the root span for `GET /api/quotes/author-stats`, and the
> cause was 301 nested SQLite spans instead of 2. The endpoint fetched all 300 quotes, then looped
> over every row and ran a separate `SELECT COUNT(*) WHERE Author = @author` per row — a classic
> N+1 query. Locally that cost about 26ms, since embedded SQLite has near-zero network latency, but
> the same shape against a networked database would take far longer. I'd fix it by replacing the
> loop with a single `GROUP BY` query that returns every author's count in one round trip.

(98 words.)

## Before / after, from the real traces

| | Before (N+1) | After (fixed) |
|---|---|---|
| Trace ID | `029aae18e16903973f96864818935f5d` | `20309a4408db1ac5eb910d5c35eb550b` |
| Total spans | 302 (1 root + 301 DB) | 3 (1 root + 2 DB) |
| Root span duration | 26.18 ms | 3.82 ms |
| DB query shape | 1x `SELECT *`, then 300x `SELECT COUNT(*) WHERE Author = @author` | 1x `SELECT *`, 1x `SELECT Author, COUNT(*) ... GROUP BY Author` |

Full detail — including sample span durations and the exact SQL text Jaeger recorded — is in
[`evidence/trace-before-summary.json`](evidence/trace-before-summary.json) and
[`evidence/trace-after-summary.json`](evidence/trace-after-summary.json).

The absolute time difference (26ms → 4ms) looks modest because embedded SQLite has essentially no
per-query network latency. The span *count* is the real story: 301 database round trips collapsed
to 2, regardless of how many quotes exist. Against a networked database with even 2–3ms of
round-trip latency per call — completely normal for Azure SQL from an App Service in the same
region — the same bug would cost 600ms–1.5s instead of 26ms, which is exactly the "add
`Thread.Sleep(1500)`" scale this exercise asks for, just reached honestly through query count
rather than a literal sleep.

## The fix

```csharp
var allQuotes = await db.Quotes.AsNoTracking().OrderBy(q => q.Id).ToListAsync(cancellationToken);

var authorCounts = await db.Quotes
    .AsNoTracking()
    .GroupBy(q => q.Author)
    .Select(g => new { Author = g.Key, Count = g.Count() })
    .ToDictionaryAsync(g => g.Author, g => g.Count, cancellationToken);

var results = allQuotes.Select(quote => new
{
    quote.Id, quote.Author, quote.Text,
    AuthorQuoteCount = authorCounts[quote.Author]
});
```

Two queries total, no matter whether there are 3 quotes or 3 million: one to list them, one
`GROUP BY` to compute every author's count in a single round trip. See
[`QuotesIntegrationApi/Program.cs`](QuotesIntegrationApi/Program.cs) for the endpoint in context,
and [`Quotes.Tests.Integration/QuotesEndpointTests.cs`](Quotes.Tests.Integration/QuotesEndpointTests.cs)
(`GetAuthorStats_WithMultipleQuotesPerAuthor_ReturnsCorrectCountPerRow`) for a correctness test —
not a performance assertion, which would be flaky, but proof the grouped query still returns the
right count per author.

## Bonus: KQL to find similar slow endpoints in App Insights

Added to [`infra/queries.kql`](infra/queries.kql). The key idea: an N+1 endpoint doesn't just run
slow, it runs a *lot* of dependency calls for one request — so instead of only sorting by
duration, aggregate dependency call count per `operation_Id` and flag requests whose *average*
dependency-call count is way above their peers':

```kql
dependencies
| where timestamp > ago(1h)
| where type == "SQL" or name has "SELECT"
| summarize dbCallCount = count() by operation_Id
| join kind=inner (
    requests
    | where timestamp > ago(1h)
    | project operation_Id, name, duration, timestamp
) on operation_Id
| summarize
    avgDbCallsPerRequest = avg(dbCallCount),
    maxDbCallsPerRequest = max(dbCallCount),
    avgDurationMs = avg(duration),
    sampleCount = count()
    by name
| where avgDbCallsPerRequest > 10
| order by avgDbCallsPerRequest desc
```

This would have flagged `GET /api/quotes/author-stats` at ~301 average DB calls per request long
before its absolute duration crossed any alert threshold.

## Reproducing this locally

```bash
docker run -d --name jaeger -p 16686:16686 -p 4317:4317 jaegertracing/all-in-one:latest
cd QuotesIntegrationApi
dotnet run --urls "http://localhost:5289"
# seed some quotes via POST /auth/token then POST /api/quotes, then:
curl http://localhost:5289/api/quotes/author-stats
# open http://localhost:16686, service = QuotesIntegrationApi
```

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/main/day-5/piece1

## Notes for mentor

No GUI/browser is available in the environment I built this in, so the "before/after trace
screenshots" the exercise asks for are real trace data pulled straight from Jaeger's own API
instead of a picture of it — same information, just JSON instead of pixels. Both trace IDs are
real and still queryable against the Jaeger container while it's running; I included the exact
`docker run` command above so this is trivially reproducible and verifiable rather than taken on
faith.

## What did I learn this session?

The absolute-duration difference from an N+1 bug depends entirely on where the database lives.
Locally against embedded SQLite, 301 queries only cost 26ms because there's no network round trip
— which means a naive "is it slow?" check by wall-clock time alone would miss this bug entirely in
local dev, and only bite once it's in production against a real network-attached database. The
span *count*, not the duration, is what made the bug undeniable in the trace — 301 nearly-identical
child spans is a shape no single well-written query produces, regardless of how fast each one runs.

## What would break this?

The first version of the fix looked up `authorCounts[quote.Author]` with the indexer, which
assumes every quote's author is present as a key — true in a single-request snapshot, but not
guaranteed if a concurrent delete removed that author's last other quote between the two queries.
Under load that's a `KeyNotFoundException` on an endpoint that used to just be slow, which is a
worse failure mode than the bug it replaced. Changed it to
`authorCounts.GetValueOrDefault(quote.Author, 0)` so a race like that degrades to "count of 0"
instead of a 500.
