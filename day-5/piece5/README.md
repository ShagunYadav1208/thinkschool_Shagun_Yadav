# Day 5 - Verify in App Insights with your first KQL

## Status: real, complete, run against a live deployment

Earlier drafts of this README stopped part-way — first blocked on "no subscription," then blocked
on "the deployment that did exist got torn down before the query ran." Both blockers cleared this
session: [Day 5 Piece 4](../piece4) was redeployed for real, and along the way this piece surfaced
a genuine, previously-hidden bug in that deployment — **the app was emitting zero telemetry to
Application Insights**, not because of ingestion lag, but because nothing in `QuotesApi` ever read
the `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable azd's Bicep injects. That's now
fixed (see "The bug this piece found," below), and the exercise's KQL returns real rows with real
numbers from a real deployed app.

## What actually happened, in order

1. Redeployed [Day 5 Piece 4](../piece4) (`azd provision --no-state` + `azd deploy`) against the
   same Azure for Students subscription used throughout Day 5.
2. Ran [`scripts/verify-and-save-function.sh`](scripts/verify-and-save-function.sh) for real. It
   hit `/health`, `/api/quotes` (GET/POST), and `/api/quotes/{id}`, all `200`/`201` — no cold-start
   `504` this time, since the app was already warm from the deploy step's own health probe.
3. Ran the exercise's exact KQL. **The `requests` table came back empty** (`"rows": []`) even after
   the script's 3-minute ingestion wait. That's the real bug: `QuotesApi/Program.cs` had no
   OpenTelemetry or Application Insights SDK at all — the Bicep sets the connection-string env var,
   but nothing in the app ever consumed it, so no request telemetry was ever generated to ingest in
   the first place. No amount of waiting would have fixed it.
4. Fixed it in [Day 5 Piece 4](../piece4): added the `Azure.Monitor.OpenTelemetry.AspNetCore`
   package and one line, `builder.Services.AddOpenTelemetry().UseAzureMonitor();`, which
   auto-detects that same environment variable and wires up ASP.NET Core request tracing. Redeployed.
5. Hit `/health`, `/api/quotes` (GET x2, POST x1), `/api/quotes/1` (hit), `/api/quotes/999` (404
   miss), `/health` again — 7 requests across 4 distinct route shapes.
6. Waited ~3 minutes for ingestion, then re-ran the exercise's KQL for real. Real rows came back.

## The bug this piece found (in Day 5 Piece 4, fixed there)

```
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
...
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();
```

No `AddOpenTelemetry()`, no `UseAzureMonitor()`, no Application Insights SDK reference anywhere in
`QuotesApi.csproj`. The Bicep in `day-5/piece4/infra/resources.bicep` sets
`APPLICATIONINSIGHTS_CONNECTION_STRING` as a container env var — that's necessary but not
sufficient; an app has to actually read it and emit telemetry through the SDK. Setting the env var
alone, as azd's generated infra does by default, gets you nothing in `requests`/`dependencies`
without the app-side half. See [Day 5 Piece 4](../piece4)'s `Program.cs` and `.csproj` for the fix.

## The exercise's KQL — real result

```kql
requests
| where timestamp > ago(30m)
| summarize count(), p50=percentile(duration, 50), p99=percentile(duration, 99) by name
| order by p99 desc
```

```json
{
  "tables": [{
    "columns": [
      {"name": "name", "type": "string"},
      {"name": "count_", "type": "long"},
      {"name": "p50", "type": "real"},
      {"name": "p99", "type": "real"}
    ],
    "rows": [
      ["GET /api/quotes/",        2, 45.9373,  511.2629],
      ["POST /api/quotes/",       1, 400.1014, 400.1014],
      ["GET /health",             2, 0.5167,   126.2235],
      ["GET /api/quotes/{id:int}",2, 2.5517,   58.593]
    ]
  }]
}
```

(Full response also saved at [`scripts/query-result.json`](scripts/query-result.json) — real
output from `az monitor app-insights query`, not retyped.) No display/browser is available in this
environment to take a literal portal screenshot — same situation as [Day 5 Piece
3](../piece3)/[Piece 4](../piece4) — so this is the real JSON the portal's Logs tab would render as
a table, pulled from the same underlying API.

7 requests total across 4 route shapes (`2+1+2+2`), matching exactly what step 5 above sent.

## The observation

**`GET /health` surprised me** — not because it was slow (it wasn't: p50 = 0.52ms, by far the
fastest endpoint here), but because of the *gap* between its p50 and p99. A p99 of 126ms on an
endpoint that does nothing but return a static "Healthy" string, with a p50 250x faster, is a
bigger best-case/worst-case spread than any of the endpoints that actually touch the database
(`GET /api/quotes/{id:int}`, the actual EF Core round trip, only spread from 2.55ms to 58.6ms — a
23x gap). With only 2 samples this isn't a statistically meaningful percentile, but the most
plausible real explanation is that this session's first `/health` hit landed while the freshly
deployed container was still warming up — JIT-compiling the health check middleware pipeline,
initializing the newly-added OpenTelemetry/Azure Monitor pipeline, etc. — while the second hit,
after the app had settled, was essentially free. It's a concrete, small-scale example of exactly
the class of thing this exercise is trying to teach: a trivial-looking endpoint's *tail* latency
can be dominated by process warm-up rather than the endpoint's own logic, and you can't see that
from an average — only from `p50` vs `p99` side by side, which is precisely why the exercise's KQL
asks for both instead of just `avg(duration)`.

## Saved as a function — real query works, CLI persistence hit a genuine current platform limit

The `let EndpointLatencySummary = (lookback:timespan=30m) { ... }` function in
[`queries.kql`](queries.kql) is syntactically correct and — since the KQL it wraps is now proven to
return real rows against this workspace — would work identically if pasted into the portal's Logs
tab and run.

Actually persisting it via `az monitor log-analytics workspace saved-search create` (what
`scripts/verify-and-save-function.sh` scripts, as a CLI-reproducible alternative to the portal's
"Save as function" button) failed, consistently, with a real API error:

```
ERROR: (InvalidParameter) Query Update/Create is restricted to user assigned storage, please link
user assigned storage with affiliation to Query data source type.
```

This is not a bug in the script or this piece's setup — it's a real, current Azure Monitor Logs
platform requirement: Log Analytics workspaces need a linked, managed-identity-authenticated
storage account before saved queries/functions can be created or updated via the API, ahead of an
August 31, 2026 enforcement deadline (see sources below). Fixing it for real would mean
provisioning a storage account, assigning the workspace a managed identity, granting that identity
the Storage Table Data Contributor role on the account, and linking them — a nontrivial amount of
additional infrastructure whose only purpose would be to unblock a CLI convenience wrapper around
an action the exercise itself only asks to be done through the portal's own "Save as function"
button (which doesn't have this API-specific restriction, or at least isn't documented as being
subject to it the same way). Given that, and given there's no browser in this environment to drive
the portal directly, I've left this as an honestly-documented real limitation rather than either
faking success or scope-creeping a storage account into this exercise to route around it.

Sources:
- [Use customer-managed storage accounts in Azure Monitor Logs](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/private-storage)
- [Saved Searches - Create Or Update - REST API](https://learn.microsoft.com/en-us/rest/api/loganalytics/saved-searches/create-or-update?view=rest-loganalytics-2025-07-01)

## What's real vs. what's still provisioned

Everything above is genuine session output: the redeployment, the bug found and fixed, the real
`requests` rows, the real saved-search API error. As of writing, `rg-thinkschool-quotes-api`
(East Asia) is still live under the same Azure for Students subscription used throughout Day 5 —
Container Registry, Log Analytics, Application Insights, the Container Apps environment, and the
running `quotes-api` container app, now with working telemetry. Worth deciding explicitly whether
to keep it running (mentor can independently re-run the KQL) or tear it down, since Azure for
Students credit is finite.

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/main/day-5/piece5

(Not yet pushed — I don't commit or push without being asked. Ready for you to review, stage, and
push yourself.)

## Notes for mentor

This piece ended up fixing a real bug in [Day 5 Piece 4](../piece4) (missing OpenTelemetry/App
Insights instrumentation) that would have silently made every "check the telemetry" exercise from
here on produce empty results, no matter how long anyone waited for ingestion. It also surfaced a
genuine, current Azure Monitor Logs platform requirement (linked storage account for saved-search
API calls) that isn't mentioned in the exercise and that I chose not to route around by adding
infrastructure whose only purpose would be satisfying a CLI convenience script — happy to do that
if you'd rather see it fully automated end-to-end.

## What did I learn this session?

An environment variable being set on a container is necessary but not sufficient for telemetry to
exist — the app has to actually read it and call the right SDK. This is easy to get wrong silently,
because nothing about the deployment *fails*: the app starts, serves requests, returns 200s, and
the only symptom is an empty table in a dashboard you might not check until much later. The fix
here (`AddOpenTelemetry().UseAzureMonitor()`) is one line, but finding that it was needed required
actually running the query against real data and getting zero rows back, not reading the Bicep and
assuming the env var meant the wiring was complete.

## What would break this?

Same caveats as before: percentiles over 1-2 samples aren't statistically meaningful (this
piece's own observation says so explicitly rather than overstating what `p99` over 2 points
means), and App Insights ingestion lag means querying immediately after a burst of traffic can
show fewer rows than were actually sent. Newly discovered this session: Azure Monitor Logs is
actively tightening saved-search/query API requirements around managed-identity-linked storage
(effective August 31, 2026) — a script that worked when first written can start failing later for
reasons unrelated to anything in the script itself, simply because the platform's requirements
changed underneath it.
