# Day 21 / Piece 1 — Verification log

All commands run locally on 2026-09-03. API on `http://localhost:5310` (SQLite, matching
`quotes-list-detail/proxy.conf.json`'s dev-proxy target), Redis in Docker on `localhost:6379`.

## 0. Environment setup

```
$ docker run -d --name quotes-redis -p 6379:6379 redis:7-alpine
ca0beb1b5933...

$ dotnet build   # QuotesApi, after adding the three NuGet packages
Build succeeded.
    2 Warning(s)   (pre-existing SQLitePCLRaw.lib.e_sqlite3 NU1903, unrelated to this change)
    0 Error(s)
```

Packages added: `Microsoft.Extensions.Caching.Hybrid` 10.9.0,
`Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.11, `Microsoft.Azure.StackExchangeRedis`
3.3.1 (the last one unused in the end - see section 1 below for why).

## 1. Redis backing: what was actually tried

Classic Azure Cache for Redis was the original plan (matching day-20's "deploy live" pattern). In
order:

```
$ az redis create --name syquotes21-redis --resource-group syquotes17-rg --location eastasia --sku Basic --vm-size c0
ERROR: (MissingSubscriptionRegistration) The subscription is not registered to use namespace 'Microsoft.Cache'.

$ az provider register --namespace Microsoft.Cache
$ az provider show --namespace Microsoft.Cache --query registrationState -o tsv
Registered   # after ~1 minute

$ az redis create --name syquotes21-redis --resource-group syquotes17-rg --location eastasia --sku Basic --vm-size c0
ERROR: (BadRequest) Azure Cache for Redis is retiring, create Azure Managed Redis instance instead.
```

Checked the replacement's price before proposing it (Retail Prices API, `armRegionName eq
'eastasia'`, SKU `B0`): `Azure_Managed_Redis_Balanced_B0`, $0.022/hr pay-as-you-go - same order of
cost as the originally-approved classic Basic C0. Proposed provisioning that instead; the user
chose local Docker Redis over any live Azure resource for this session. No Azure Cache/Managed
Redis resource exists as a result of this session - only the `Microsoft.Cache` provider
registration (free, one-time per subscription) was performed.

## 2. Cache wiring: manual proof it works

API started fresh, one quote created:

```
$ curl -s -X POST http://localhost:5299/api/quotes/ -H "Content-Type: application/json" \
    -d '{"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal."}'
{"id":1,"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal."}

$ curl -s http://localhost:5299/api/cache/metrics
{"dbReads":0,"cacheRequests":0,"cacheMisses":0,"cacheHits":0,"hitRatePercent":0}

$ time curl -s http://localhost:5299/api/quotes/1     # cold - pays the simulated 150ms cost
{"id":1,"author":"Ada Lovelace","text":"..."}
real  0m0.494s

$ time curl -s http://localhost:5299/api/quotes/1     # warm - near-instant
{"id":1,"author":"Ada Lovelace","text":"..."}
real  0m0.036s

$ curl -s http://localhost:5299/api/cache/metrics
{"dbReads":1,"cacheRequests":2,"cacheMisses":1,"cacheHits":1,"hitRatePercent":50}

$ docker exec quotes-redis redis-cli KEYS "*"
quotes:quote:1
```

The Redis `KEYS` check confirms the entry is really in the L2 tier, not just held in the L1
in-process copy.

## 3. Stampede protection: manual proof (before the k6 script existed)

```
$ curl -s -X POST http://localhost:5299/api/cache/evict/1
$ curl -s -X POST http://localhost:5299/api/cache/metrics/reset
$ for i in $(seq 1 30); do curl -s -o /dev/null http://localhost:5299/api/quotes/1 & done; wait
$ curl -s http://localhost:5299/api/cache/metrics
{"dbReads":1,"cacheRequests":30,"cacheMisses":1,"cacheHits":29,"hitRatePercent":96.67}

$ curl -s -X POST http://localhost:5299/api/cache/metrics/reset
$ for i in $(seq 1 30); do curl -s -o /dev/null http://localhost:5299/api/quotes/1/uncached & done; wait
$ curl -s http://localhost:5299/api/cache/metrics
{"dbReads":30,"cacheRequests":0,"cacheMisses":0,"cacheHits":0,"hitRatePercent":0}
```

30 concurrent requests: 1 DB read through the cache, 30 without it.

## 4. Full load test (k6)

Two Git-Bash-on-Windows gotchas hit while building `load-test/run-load-test.sh`, both fixed in the
script itself:

- **MSYS path conversion** mangled any env var starting with `/` (e.g. `TARGET_PATH=/api/quotes/1`
  became a bogus local-path-prefixed string) - fixed with `MSYS_NO_PATHCONV=1` and
  `MSYS2_ARG_CONV_EXCL="*"` at the top of the script.
- **k6 and node are native Windows binaries** and can't resolve Git Bash's `/c/...`-style POSIX
  paths - fixed by using `pwd -W` (a Git Bash builtin) for every path handed to them.
- **k6's JSON summary schema is flat**, not nested under a `.values` key (e.g.
  `metrics.http_req_duration['p(99)']`, not `metrics.http_req_duration.values['p(99)']`) - the
  first draft of the parser assumed the nested shape and threw; fixed after inspecting a real
  export with `node -e "console.log(JSON.stringify(...))"`.
- **`curl -o /dev/null` failed (exit 23)** with `MSYS_NO_PATHCONV=1` set, because that env var also
  stops the native-Windows curl binary from resolving `/dev/null` to `NUL`. Fixed by using shell
  redirection (`curl ... > /dev/null`) instead of curl's own `-o` flag for that one call.

Full run, quote id 1, 50 VUs:

```
== Day 21: HybridCache load test ==
Base URL: http://localhost:5310   Quote id: 1

--- 1) BASELINE (uncached): 50 VUs for 20s ---
DB reads: 6448   HTTP req/s: 319.99   p99: 172.03ms

--- 2) CACHED (HybridCache): 50 VUs for 20s ---
DB reads: 0   HTTP req/s: 8052.54   p99: 46.34ms

--- 3) STAMPEDE PROTECTION: 50 concurrent requests at a cold key ---
  a) cached endpoint (single-flight expected)
     50 concurrent requests -> 1 DB read(s)
  b) uncached endpoint (no protection, expect ~50)
     50 concurrent requests -> 50 DB read(s)

== Summary ==
                                 Uncached       Cached
DB reads (sustained run)             6448            0
HTTP req/s                         319.99      8052.54
p99 latency (ms)                   172.03        46.34
DB reads under 50-way stampede           50            1
```

## 5. Frontend: Cache tab, headless-browser verification

Angular dev server (`ng serve`, port 4200) against the API on port 5310. Verified with Playwright
(chromium, headless) rather than `chromium-cli` (not installed in this environment) - script at
`C:\Users\shagu\AppData\Local\Temp\claude\...\scratchpad\pw-check\check-cache-tab.js` (session
scratchpad, not part of the repo).

Sequence driven: navigate → click "Cache" tab → wait for "Live cache counters" → screenshot →
click "Fetch (cached)" → wait for result → screenshot → click "Fetch (uncached)" → wait for result
→ screenshot → click "Evict this id" → click "Run stampede test" → wait for the button to read
"Running..." then wait for it to revert to "Run stampede test" (a naive `waitForSelector` on
result text false-positived on the always-present static hint paragraph the first time - fixed by
asserting on the button's own busy/idle text instead) → screenshot → read `console --errors`
equivalent (`page.on('console'/'pageerror')`).

Result: **zero console errors** across the whole sequence. Final stampede screenshot showed the
counters panel reading `dbReads: 1, cacheRequests: 30, cacheMisses: 1, cacheHits: 29,
hitRatePercent: 96.67` and the stampede-result panel reading "30 concurrent requests / 1 DB read"
(green, `result--good`). Screenshots copied into [screenshots/](screenshots/):
`cache-tab-initial.png`, `cache-tab-fetch-cached.png`, `cache-tab-fetch-uncached.png`,
`cache-tab-stampede.png`.

## 6. Live deployment

User asked to deploy live and provide the link. Redis question reopened: local Docker isn't
reachable from Azure, so live means a real Azure Redis. Confirmed cost with the user again before
provisioning (Azure Managed Redis `Balanced_B0` this time, not classic - see section 1) and again
on lifecycle (delete after verification vs. keep running - "delete after" implied by not choosing
the explicit "keep running" option).

```
$ az redisenterprise create --cluster-name syquotes21-redis --resource-group syquotes17-rg \
    --location eastasia --sku Balanced_B0 --access-keys-auth Enabled
ERROR: (BadRequest) 'properties.publicNetworkAccess' is required in API version 2025-07-01.

$ az redisenterprise create --cluster-name syquotes21-redis --resource-group syquotes17-rg \
    --location eastasia --sku Balanced_B0 --access-keys-auth Enabled --public-network-access Enabled
{ "provisioningState": "Succeeded", "resourceState": "Running", "port": 10000, ... }
```

Backend deploy (day-19/20's documented pattern - see day-19/piece1/verification-log.md and
day-20/piece1's README section 9): `dotnet publish -c Release`, repacked via a from-scratch
`zip_unix.py` (the original day-17 script no longer exists on disk; rewritten from the
verification-log.md description - `zipfile` module, `create_system = 3`, explicit Unix
`external_attr` permission bits), `az webapp deploy --type zip` to `syquotes17-api`. Frontend:
`ng build --configuration production`, deployed via `@azure/static-web-apps-cli` (`npx`) to
`syquotes17-swa`.

**One permission-classifier block, resolved by asking the user:** `az staticwebapp secrets list`
(fetching the SWA deployment token) was auto-blocked as a credential-fetch action. Asked the user
directly; approved. Fetched the token to a local file, used it once
(`SWA_CLI_DEPLOYMENT_TOKEN=$(...) npx @azure/static-web-apps-cli deploy ...`), never printed it,
deleted the file immediately after.

Both deploys succeeded cleanly:

```
$ az webapp deploy ... --type zip
WARNING: Status: Site started successfully. Time: 106(s)
WARNING: Deployment has completed successfully

$ SWA_CLI_DEPLOYMENT_TOKEN=... npx @azure/static-web-apps-cli deploy ... --env production
✔ Project deployed to https://black-desert-0fde3f100.7.azurestaticapps.net 🚀
```

Redis wired via `az webapp config appsettings set --settings Redis__ConnectionString=<host>:10000,password=<key>,ssl=True,abortConnect=False`
(access key fetched via `az redisenterprise database list-keys`, piped straight into the app
setting, never echoed to a log or this file).

## 7. Live: Redis was unusable

First request after wiring Redis: `GET /api/quotes/5` took **18s**. Second request (expected to
be an L1 hit, near-instant): **108.9s**. Metrics after both: `cacheRequests: 1` - meaning the
counters themselves reset between the two curls, implying the process restarted mid-sequence.
Repeated the test after backing off and retrying several times over ~10 minutes; latencies ranged
8s-29s per request, with at least one request exceeding 60s and one exceeding 30s inside a stampede
burst. Root cause not conclusively identified (candidates in README.md section 5) - given a live
demo was the actual goal, stopped debugging and disabled Redis instead of continuing indefinitely:

```
$ az webapp config appsettings delete --resource-group syquotes17-rg --name syquotes17-api \
    --setting-names Redis__ConnectionString
```

Post-disable, response times dropped to 0.86s-7.9s per request - still slower than local (plausible
Basic-tier/cross-region overhead, not a hang) but no more multi-second-to-multi-minute stalls.

## 8. Live: the `RemoveAsync` 500, and the real bug underneath it

Even after disabling Redis, `POST /api/cache/evict/{id}` kept failing (`500`, occasionally an
outright hang/connection-reset rather than a clean response). First hypothesis - `HybridCache`'s
`DefaultHybridCache` throwing when literally zero `IDistributedCache` is registered - was
plausible and testable locally:

```
$ docker stop quotes-redis
$ Redis__ConnectionString="" dotnet run --urls http://localhost:5311   # AddDistributedMemoryCache fallback added
$ curl -X POST http://localhost:5311/api/cache/evict/1
HTTP 204   # fixed locally
```

Rebuilt `InfrastructureExtensions.cs` with that `AddDistributedMemoryCache()` fallback, redeployed,
retested live - **still 500**. That ruled out "zero L2 registered" as the live root cause (since
the fallback should have caught it) and pointed at something else: the App Service Application
Setting `Redis__ConnectionString` (deleted, not set-to-empty) simply *removes an override* - it
doesn't force the value to empty. `appsettings.json`'s base `"Redis": {"ConnectionString":
"localhost:6379"}` was still the effective value in Production, because
`appsettings.Production.json` never overrode it. So Production was trying to reach
`localhost:6379` from inside its own Linux container - nothing listens there.

```
$ az webapp config appsettings set --resource-group syquotes17-rg --name syquotes17-api \
    --settings 'Redis__ConnectionString='          # explicit empty, not deleted
$ az webapp restart --resource-group syquotes17-rg --name syquotes17-api
$ curl -X POST https://syquotes17-api.azurewebsites.net/api/cache/evict/5
HTTP 500   # still failing - see below
```

Still failing even with the explicit override confirmed live (`az webapp config appsettings list`
showed `Redis__ConnectionString: ""`). Downloaded the App Service's logs
(`az webapp log download`) to look for the actual exception rather than guessing further, and
found something unrelated but important in `docker.log`: a startup probe failure and a full
container stop/restart cycle (`"Site startup probe failed after 61.8214655 seconds"` ...
`"Site: syquotes17-api stopped."`) timestamped during the run of rapid-fire config
changes/restarts (delete setting → restart → set-to-empty → restart, all within a few minutes).
The App Service was very likely still recovering from that instability, not purely serving a
config bug.

Fixed the config bug at its source (`appsettings.Production.json` now explicitly sets
`"Redis": {"ConnectionString": ""}`), did **one clean full publish + redeploy** rather than another
rapid-fire settings change, and gave the App Service a genuine ~2-minute settle window
(`ScheduleWakeup`, not a tight retry loop) before testing again:

```
$ curl -X POST https://syquotes17-api.azurewebsites.net/api/cache/evict/5
HTTP 204   # x3 in a row, ~0.8-1.1s each
```

Fixed. Full stampede re-verification, clean:

```
$ curl -X POST https://syquotes17-api.azurewebsites.net/api/cache/evict/5
$ curl -X POST https://syquotes17-api.azurewebsites.net/api/cache/metrics/reset
$ for i in $(seq 1 20); do curl -s -o /dev/null https://syquotes17-api.azurewebsites.net/api/quotes/5 & done; wait
$ curl https://syquotes17-api.azurewebsites.net/api/cache/metrics
{"dbReads":1,"cacheRequests":20,"cacheMisses":1,"cacheHits":19,"hitRatePercent":95}
```

And the live frontend, re-run with Playwright against
`https://black-desert-0fde3f100.7.azurestaticapps.net`: zero console errors, final screenshot
showing "20 concurrent requests / 1 DB read" rendered live. Screenshots in
[screenshots/](screenshots/): `live-cache-tab-initial.png`, `live-cache-tab-fetch-cached.png`,
`live-cache-tab-fetch-uncached.png`, `live-cache-tab-stampede.png`.

## 9. Cleanup

- Local: `docker stop quotes-redis` / `docker rm quotes-redis` not yet run - left running for the
  user's own continued local testing.
- Azure: `syquotes21-redis` (Azure Managed Redis, `Balanced_B0`) still exists and still bills
  (~$0.022/hr) - not deleted yet. It isn't used by the live deployment (Redis is disabled there
  per section 7), so every hour it stays up is pure unused cost. Left for the user to decide:
  delete it now (matches the original "delete after verification" agreement), or keep it around to
  continue debugging the connectivity problem separately from this exercise's own scope.
- `Microsoft.Cache` resource-provider registration (section 1): free, one-time, left registered -
  no reason to undo it.
