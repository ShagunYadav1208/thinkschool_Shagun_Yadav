# Day 21 / Piece 1 — HybridCache + stampede protection

Backend is [day-20/piece1](../../day-20/piece1)'s `QuotesApi`, copied unmodified into
[QuotesApi/](QuotesApi/) so the cache layer could be added without touching the read-only
original. Frontend is day-20/piece1's Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/) with one new tab: **Cache**.

## Current status

**Deployed and verified live**, both frontend and backend, on day-17's existing infrastructure:

- **Frontend:** https://black-desert-0fde3f100.7.azurestaticapps.net (the **Cache** tab)
- **API:** https://syquotes17-api.azurewebsites.net

**Running L1-only in production** (in-process HybridCache, no Redis L2). Azure Managed Redis
(`Balanced_B0`) was provisioned for this deployment, but the App Service couldn't reach it
reliably - individual requests that touched it took anywhere from 8s to nearly 2 minutes, some
never completed at all. Rather than ship a live demo built on that, Redis was disabled
(`Redis:ConnectionString` set to empty) and the site now runs the same L1-only configuration
already proven correct locally and in section 4 below - fast, stable, and still demonstrating
real HybridCache stampede protection (single-flight coalescing is an L1 property; it doesn't need
an L2 to work). See section 5 for what was actually observed with Managed Redis, and section 6 for
a real bug this deployment caught that local testing never could.

Verified locally, separately, against a real local Redis (Docker) throughout development - see
section 3 (load test) and section 4 (stampede) below, both run against that local setup before
deployment.

## 1. The cache wiring

**Registration** (`Extensions/InfrastructureExtensions.cs`):

```csharp
services.Configure<CacheDemoOptions>(configuration.GetSection(CacheDemoOptions.SectionName));
services.AddSingleton<ICacheMetrics, CacheMetrics>();

services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromSeconds(30),      // L2 (Redis) entry lifetime
        LocalCacheExpiration = TimeSpan.FromSeconds(30), // L1 (in-process) entry lifetime
    };
});

// L2: Redis. Local Docker container for this session (see brief-to-agent.md). HybridCache
// picks up an L2 automatically the moment an IDistributedCache is registered - no other wiring.
var redisConnectionString = configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "quotes:";
    });
}
```

**The hot read** (`Extensions/QuoteEndpointExtensions.cs`, `GET /api/quotes/{id}`):

```csharp
group.MapGet("/{id:int}", async (
    int id,
    HybridCache cache,
    IQuoteRepository repository,
    ICacheMetrics metrics,
    CancellationToken cancellationToken) =>
{
    metrics.RecordCacheRequest();

    var quote = await cache.GetOrCreateAsync(
        QuoteCacheKeys.ById(id),
        async ct =>
        {
            metrics.RecordCacheMiss();
            return await repository.GetByIdAsync(id, ct);
        },
        cancellationToken: cancellationToken);

    return quote is null ? Results.NotFound() : Results.Ok(quote);
});
```

`GetOrCreateAsync`'s single-flight behavior is what gives the stampede protection: while the
factory above is running for a given key, any other caller asking for that *same* key gets handed
the same in-flight `Task` instead of starting a second DB read. `IQuoteRepository.GetByIdAsync`
(`Repositories/QuoteRepository.cs`) is the only place a real DB round trip happens - it increments
`ICacheMetrics.DbReads` and applies a 150ms simulated latency (`CacheDemo:SimulatedDbLatencyMs`),
standing in for "this row costs something real to fetch" so the stampede window is wide enough to
actually observe under load, not resolved inside a single tick.

**Invalidation:** `DELETE /api/quotes/{id}` now also calls `cache.RemoveAsync(...)` so a deleted
quote doesn't keep serving from a stale cache entry for the rest of its TTL. A manual eviction
endpoint (`POST /api/cache/evict/{id}`) exists for the demo/load-test harness to force a guaranteed
cold key without waiting out the TTL.

**Comparison endpoint:** `GET /api/quotes/{id}/uncached` calls the same repository method directly,
bypassing the cache entirely - added purely so the load test below has an honest "before" to
compare the cached "after" against, using the identical request shape.

## 2. The load test

`load-test/run-load-test.sh` (k6 + a small Node-based JSON parser, no other dependencies) runs,
against the running API:

1. **Baseline** - `k6 run load-test/hot-read.js` at 50 constant VUs for 20s against
   `/api/quotes/1/uncached`, reading `ICacheMetrics.DbReads` before/after via
   `GET /api/cache/metrics`.
2. **Cached** - the same 50 VUs / 20s run against `/api/quotes/1` (cache pre-warmed once, outside
   the timed window).
3. **Stampede** - `load-test/stampede.js`'s `shared-iterations` executor fires 50 VUs as one burst
   (each doing exactly one request) at a freshly-evicted key, once against the cached endpoint and
   once against the uncached one, reading `DbReads` after each.

Run it yourself (API running locally, Redis container up): `cd load-test && ./run-load-test.sh`.

## 3. Before / after

Real run, captured 2026-09-03, API on `localhost:5310`, Redis in Docker on `localhost:6379`,
quote id 1 ("That brain of mine is something more than merely mortal." — Ada Lovelace):

| | Uncached (baseline) | Cached (HybridCache) |
|---|---:|---:|
| DB reads over the 20s run | 6,448 | 0 |
| HTTP requests/sec | 319.99 | 8,052.54 |
| p99 latency | 172.03ms | 46.34ms |

- **DB reads: 6,448 → 0.** Every one of the 6,448 uncached requests hit the DB (and paid the
  150ms simulated cost); the cached run's first request populated the cache and the other ~46,000+
  requests inside the same 20s window (see the req/s column) never touched the DB again - the
  cache entry's 30s TTL outlives the 20s test window, so the whole sustained run after the first
  request is pure L1 hits.
- **Throughput: 320 → 8,053 req/s (~25x).** The uncached run is bottlenecked by the 150ms
  simulated DB cost per request (50 VUs / 0.15s ≈ 333 req/s, which is what's observed); the cached
  run is bottlenecked by nothing but in-process dictionary lookups and HTTP overhead.
- **p99: 172ms → 46ms (~3.7x).** The uncached p99 sits just above the 150ms floor (queuing +
  network); the cached p99 is almost entirely HTTP/ASP.NET Core overhead, not cache lookup cost.

## 4. Stampede protection, proven twice

**Via k6** (`load-test/stampede.js`, 50 VUs, `shared-iterations`, same run as above):

| | Uncached | Cached |
|---|---:|---:|
| DB reads after a 50-way concurrent burst at a cold key | 50 | 1 |

Without protection, every one of the 50 concurrent requests reaches a cold key and each starts its
own DB read - 50 reads for one logical piece of data. With HybridCache, the first request's
`GetOrCreateAsync` call starts the factory (the real DB read, plus the 150ms simulated cost); the
other 49, arriving while that factory is still in flight, are handed the same pending `Task`
instead of starting their own - the DB sees exactly **1** read no matter how many concurrent
callers asked for the same key at once.

**Live in the browser**, via curl against the raw API before the UI existed:

```
$ curl -s -X POST http://localhost:5310/api/cache/metrics/reset
$ curl -s -X POST http://localhost:5310/api/cache/evict/1
$ for i in $(seq 1 30); do curl -s -o /dev/null http://localhost:5310/api/quotes/1 & done; wait
$ curl -s http://localhost:5310/api/cache/metrics
{"dbReads":1,"cacheRequests":30,"cacheMisses":1,"cacheHits":29,"hitRatePercent":96.67}
```

30 truly concurrent (bash-backgrounded, same instant) requests to a cold key → 1 DB read, 1 cache
miss, 29 cache hits. The same burst against `/api/quotes/1/uncached` (no cache in the path at all)
produced `{"dbReads":30,...}` - one DB read per request, exactly as expected without protection.

**And in the new Cache tab itself** - "Run stampede test" evicts the key, resets the counters, then
fires N concurrent requests from the browser via `forkJoin`, reading the counters back afterward.
Screenshot: [screenshots/cache-tab-stampede.png](screenshots/cache-tab-stampede.png) - 30 concurrent
requests, 1 DB read, rendered live from the same `ICacheMetrics` the load test reads. Also see
[screenshots/cache-tab-initial.png](screenshots/cache-tab-initial.png),
[cache-tab-fetch-cached.png](screenshots/cache-tab-fetch-cached.png), and
[cache-tab-fetch-uncached.png](screenshots/cache-tab-fetch-uncached.png) for the single-read
comparison (cached fetch returns near-instantly once warm; uncached always pays the ~157-161ms
simulated cost). Verified with a headless-browser pass (Playwright) - no console errors, every
button drives a real HTTP call against the running API.

**And live in production**, same sequence against the deployed API:

```
$ curl -s -X POST https://syquotes17-api.azurewebsites.net/api/cache/evict/5
$ curl -s -X POST https://syquotes17-api.azurewebsites.net/api/cache/metrics/reset
$ for i in $(seq 1 20); do curl -s -o /dev/null https://syquotes17-api.azurewebsites.net/api/quotes/5 & done; wait
$ curl -s https://syquotes17-api.azurewebsites.net/api/cache/metrics
{"dbReads":1,"cacheRequests":20,"cacheMisses":1,"cacheHits":19,"hitRatePercent":95}
```

20 concurrent requests against the live App Service → 1 DB read. Same Playwright pass repeated
against the live frontend: [screenshots/live-cache-tab-stampede.png](screenshots/live-cache-tab-stampede.png)
("20 concurrent requests / 1 DB read", live). Also
[live-cache-tab-initial.png](screenshots/live-cache-tab-initial.png),
[live-cache-tab-fetch-cached.png](screenshots/live-cache-tab-fetch-cached.png), and
[live-cache-tab-fetch-uncached.png](screenshots/live-cache-tab-fetch-uncached.png).

## 5. Deploying live: what Azure Managed Redis actually did

Provisioned `syquotes21-redis` (Azure Managed Redis, `Balanced_B0`, `eastasia`, same resource
group as the rest of this app's infrastructure) specifically to deploy this live with a real L2,
matching day-20's "deployed and verified live" pattern. Wired via access-key auth (App Service
Application Setting `Redis__ConnectionString`, never committed to source) since Entra ID
data-plane auth for Managed Redis was more setup than this session's time budget justified.

**What happened once the App Service actually tried to use it:** requests that touched Redis took
anywhere from 8 seconds to nearly 2 minutes - some never completed at all within a 30s client
timeout. This wasn't a one-time cold-start cost; it recurred across a fresh restart and repeated
retries. Root cause not conclusively identified (candidates: `OSSCluster` clustering policy
interacting badly with `StackExchange.Redis`'s cluster-topology discovery on first connect; the
App Service's outbound network path to a `.redis.azure.net` endpoint on a non-standard port
(10000) being slow specifically from this Basic-tier plan; or something particular to this
region/SKU combination) - **not** exercised locally at all, since local testing only ever used a
plain Docker Redis on `localhost:6379` with no clustering and no TLS.

**Decision:** disabled Redis for this deployment (`Redis:ConnectionString` empty) rather than ship
a live site that hangs. HybridCache's L1-only mode is fully proven (sections 3-4, and the live
stampede check above) and the app's actual behavior in production is fast and correct either way -
just without a real L2 backing it live. The Managed Redis resource still exists as of this
writing; see "Notes for mentor" for the cost/cleanup question this raises.

## 6. A real bug this deployment caught that local testing couldn't

Setting `Redis:ConnectionString` to empty via the App Service's Application Settings should have
been enough on its own to disable Redis - and updates an environment variable, which .NET
configuration is documented to prioritize over `appsettings.json`. It wasn't: `POST
/api/cache/evict/{id}` kept returning `500`s (and sometimes hanging) even after that setting was
confirmed empty and the app restarted.

**Root cause:** `appsettings.json` (the shared base file, not environment-specific) hardcodes
`Redis:ConnectionString` to `localhost:6379` - the correct value for local development, wrong
everywhere else. `appsettings.Production.json` never overrode it. Empty-string environment
variables and JSON config both participate in the same configuration tree, and in this specific
case the App Service's restart timing meant the file-based `localhost:6379` was still what
`InfrastructureExtensions.cs` saw at the moment it decided whether to register Redis - so
Production was silently trying to reach `localhost:6379` **from inside the App Service's own
container**, where nothing is listening. `HybridCache.GetOrCreateAsync` tolerates a failing L2
gracefully (catches the exception, falls back to L1-only) - which is exactly why `GET
/api/quotes/{id}` kept working throughout and made this look like a Redis-network problem rather
than a config problem. `HybridCache.RemoveAsync` does not have the same tolerance; it let the
connection failure (or hang) propagate as an unhandled exception.

**Fix:** `appsettings.Production.json` now explicitly sets `Redis:ConnectionString` to `""`
(committed - see the file), so Production never depends on the local default even before any
App Service Application Setting is considered. `InfrastructureExtensions.cs` also now explicitly
registers `AddDistributedMemoryCache()` as an L2 placeholder whenever no real Redis connection
string is configured, matching HybridCache's own documented pattern for "no real L2 available" -
belt-and-suspenders with the config fix, and independently confirmed (locally, with Redis stopped)
to resolve the same `RemoveAsync` failure on its own.

Caught only because this was actually deployed and exercised end-to-end against a real config
layering (base + environment + App Service settings) - nothing about it was visible from `dotnet
build`, the local test suite, or any of this session's local load testing, all of which always had
either a working Redis or an explicitly-set empty connection string in front of them, never the
"silently wrong default" case Production hit.

## What did I learn this session?

1. **A realistic stampede test needs the DB read to be slow enough to matter.** SQLite reading one
   row by primary key is sub-millisecond - a concurrent burst against it would resolve inside a
   single tick and the "protection" would be unobservable either way, cached or not. The 150ms
   simulated latency isn't padding for its own sake; it's what makes the miss window wide enough
   for 30-50 truly concurrent callers to actually race each other before the first one populates
   the cache, so the metrics numbers mean something.
2. **HTTP/1.1's per-host connection limit changes what a browser-driven "concurrent burst" actually
   looks like on the wire**, even though it never affected the *result* here. Chromium caps
   outbound connections to one origin at 6; firing 30 requests via `forkJoin` in the Cache tab
   queues them in waves rather than opening 30 sockets at once. It didn't matter for correctness -
   HybridCache's single-flight window covers any requests that overlap the pending factory call,
   not literally-simultaneous ones - but it did make my first Playwright verification pass
   misleading (a `waitForSelector` on text that was already present in a static hint resolved
   before the async work finished, so I nearly reported success on a screenshot that still read
   "Running...").
3. **`HybridCache.GetOrCreateAsync` and `RemoveAsync` don't fail the same way when L2 is broken.**
   `GetOrCreateAsync` swallows an L2 exception and quietly serves from L1 - which is good
   resilience, but it also means a broken Redis connection can hide behind "everything looks
   fine" for the one operation you're most likely to test (a read), right up until something that
   *doesn't* have that tolerance (`RemoveAsync`, in this case) hits the same broken connection and
   surfaces as a 500. If I'd only tested `GET` live, I'd have shipped this thinking Redis-down
   resilience was fully proven when only half of it was.
4. **A config value with a working local default is more dangerous than one with no default at
   all.** `Redis:ConnectionString: "localhost:6379"` in the shared `appsettings.json` was
   *correct* for every local run this session did, which is exactly why it took a live deployment
   to notice `appsettings.Production.json` never overrode it - a missing/empty default would have
   failed loudly and immediately instead of silently resolving to a wrong-but-plausible value.

## What would break this

- **Caching a "not found" result.** `GetOrCreateAsync` happily caches a `null` `Quote?`. If a quote
  is requested (404), then created moments later with the same id (not possible today since ids are
  DB-generated and monotonic, but true in general for any cache keyed by a reusable identifier),
  the 404 could keep being served from cache for up to the remaining TTL. Not exercised here because
  this app's ids can't be reused, but it's a real sharp edge in the pattern.
- **A single-process view of "stampede protection."** `HybridCache`'s single-flight coalescing is
  per-process (per L1). If `QuotesApi` scales to multiple instances, each instance's L1 still
  collapses its *own* concurrent misses to one DB read, but N instances hitting a cold key at the
  same moment still produce up to N DB reads - one per instance - even though Redis (L2) would
  otherwise let them share a warm entry once any one instance populates it. Redis itself doesn't
  coalesce concurrent misses across processes; only a distributed lock would.
- **The 30s TTL is arbitrary and untuned.** It was picked to safely outlast the 20s sustained load
  test, not derived from how often quote data actually changes. A production system would pick this
  from real staleness tolerance, and would likely also need a shorter L1 TTL than L2 (a longer-lived
  Redis copy, refreshed into a shorter-lived local copy) rather than the two being equal.
- **No circuit breaker for a Redis that's slow rather than absent - and this session actually hit
  it.** Section 5's Managed Redis instance wasn't down; it was reachable but slow (8s-2min per
  operation). `GetOrCreateAsync` doesn't distinguish "L2 threw immediately" from "L2 is about to
  throw after a long hang" - a slow-but-technically-working L2 adds its full latency to every L1
  miss rather than being skipped quickly. The fix here was operational (disable Redis) rather than
  code-level (e.g. a short `IDistributedCache` timeout wrapper that fails fast past some threshold)
  - a real production system talking to a Redis with variable network quality would want that
  timeout wrapper, not just "works or doesn't."
- **The `AddDistributedMemoryCache()` fallback is per-process, same as L1.** It doesn't add real
  L2 semantics (no cross-instance sharing) - it exists purely so `HybridCache`'s L2-assuming code
  paths (like `RemoveAsync`, per section 6) have *something* registered. Don't mistake "no crash"
  for "has a working second tier."

## Notes for mentor

- `day-20/piece1` (and everything upstream of it) was read-only reference / copy source - nothing
  there was modified. Any file needed from it was copied into `day-21/piece1` first.
- **Deployed live** onto day-17's existing App Service and Static Web App, same as day-19/day-20 -
  see "Current status" above for the links. Running L1-only in production; section 5 explains why
  (Managed Redis was too unreliable from this App Service to ship live) and section 6 documents a
  real config bug this deployment caught that no amount of local testing would have found.
- **Open item: the `syquotes21-redis` Azure Managed Redis resource is still running** and billing
  (`Balanced_B0`, ~$0.022/hr) despite not being used by the live deployment - it was provisioned
  for this session's live demo, turned out to be unusable from this App Service, and hasn't been
  deleted yet pending the user's call on whether to keep debugging it or tear it down.
- Full command transcripts (the k6 runs, the manual curl stampede proof, the Playwright
  verification pass - local and live - and the live debugging session in sections 5-6) are in
  [verification-log.md](verification-log.md).

## GitHub link

Not pushed yet - link to follow once pushed to the `thinkbridge-thinkschool` org, per this user's
standing preference that git actions (including read-only ones) need explicit permission each time.
