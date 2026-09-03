# Brief to the agent (Claude Code)

**Exercise (Day 21 - HybridCache + stampede protection):** add HybridCache (in-memory + Redis) to
a hot read, with stampede protection so a cache miss doesn't fan out N identical DB hits. Measure
the hit rate and the DB load drop under concurrent load.

**Where:** `day-21/piece1`. Backend is [day-20/piece1](../../day-20/piece1)'s `QuotesApi`, copied
unmodified into [QuotesApi/](QuotesApi/) so the cache layer could be added without touching the
read-only original. Frontend is day-20/piece1's Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/) with one new tab added: **Cache**.

**What changed from day-20's read path:** `GET /api/quotes/{id}` used to call
`IQuoteRepository.GetByIdAsync` directly on every request - a real DB round trip every time, even
for the same quote requested a thousand times a second. Day 21 puts `HybridCache` in front of it:
an in-process L1 (always on) plus a Redis L2 (local Docker for this session - see "Redis backing"
below). `HybridCache.GetOrCreateAsync`'s single-flight behavior means concurrent misses on the
*same* key share one in-flight DB read instead of each starting their own - that's the stampede
protection the exercise asks for, and it's proven with real counters, not asserted.

**Do not modify** `day-20/piece1` (or anything upstream of it) - read-only reference / copy source,
same rule every prior day has used. Anything needed from it was copied into `day-21/piece1` first,
never edited in place.

## Redis backing

Classic Azure Cache for Redis is retired on this subscription (`az redis create` returns
`BadRequest: Azure Cache for Redis is retiring, create Azure Managed Redis instance instead`).
Development and initial local verification used a local Docker Redis container instead - faster
to iterate, zero cost, and HybridCache's own code is identical either way (it only ever talks to
`IDistributedCache`, never to Redis directly - see `InfrastructureExtensions.cs`).

The user later asked for a live Azure deployment. That requires a Redis the App Service can
actually reach, so `syquotes21-redis` (Azure Managed Redis, `Balanced_B0`, ~$0.022/hr) was
provisioned in `syquotes17-rg` after explicit cost confirmation. Wiring it up live surfaced two
real problems, documented in full in README.md sections 5-6 and verification-log.md:

1. **Managed Redis was unreliable from this App Service** - individual requests touching it took
   8 seconds to nearly 2 minutes, some never completed. Root cause not conclusively pinned down;
   disabled rather than shipped broken.
2. **A real config bug**, caught only by deploying: `appsettings.json`'s local-dev Redis default
   (`localhost:6379`) was never overridden in `appsettings.Production.json`, so Production was
   silently trying to reach `localhost:6379` from inside its own container. `GetOrCreateAsync`
   tolerates that failure silently (masking it); `RemoveAsync` doesn't, and 500'd. Fixed at the
   config level (explicit empty override) and defensively (`AddDistributedMemoryCache()` as an L2
   placeholder whenever no real Redis is configured).

**Current live state:** deployed and verified working end-to-end, running L1-only (Redis
disabled). The `syquotes21-redis` resource still exists and still bills - not yet deleted, pending
the user's decision (keep debugging the connectivity issue, or tear it down).

## The exercise's own gates

- Paste the cache wiring + the load-test before/after (DB queries/sec, p99) - see README.md
  sections 1-3, backed by a real k6 run in verification-log.md.
- Show stampede protection working under concurrency - see README.md section 4, proven twice: once
  via k6 (`load-test/stampede.js`) and once live in the browser's new Cache tab (screenshots in
  [screenshots/](screenshots/)).
