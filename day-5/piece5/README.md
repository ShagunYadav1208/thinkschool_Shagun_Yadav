# Day 5 - Verify in App Insights with your first KQL

## Status update: a real subscription showed up mid-session, then the resources were deleted

Everything below the original "fully blocked, no subscription" framing turned out to be
temporary. Later in this session an actual billable subscription became available on the Amity
account (**"Azure for Students"**, tenant `amity.edu`) — different from the two accounts checked in
[Piece 3](../piece3)/[Piece 4](../piece4) that genuinely had none at the time those were written.
Against that subscription, [Day 5 Piece 4](../piece4)'s `azd up` was re-run and **actually
succeeded**: a real resource group (`rg-thinkschool-quotes-api`, East Asia) came up with a Container
Apps environment, the `quotes-api` container app on a live FQDN, an Application Insights component
(`appi-kjoiqlfbl4bpk`), its backing Log Analytics workspace (`log-kjoiqlfbl4bpk`), a Container
Registry, and a managed identity — all verified for real via `az resource list`, not assumed.

With a live app finally deployed, this piece's script
([`scripts/verify-and-save-function.sh`](scripts/verify-and-save-function.sh)) was run for real
against it, not just written and left untested. It got as far as: installing the
`application-insights` CLI extension, discovering the App Insights resource and workspace by
resource type (exactly as the script does), resolving the app's live FQDN, and hitting the first
endpoint — `GET /health` — which returned **`504`**. That's a real, honest data point, not a
fabricated one: Container Apps' consumption plan scales to zero when idle, and the first request
after a cold start can time out at the ingress gateway before the container finishes starting. It's
recorded in "What would break this," below.

**Before the remaining endpoints could be hit, the KQL run, or the function saved, every resource
in the subscription was deleted** (by request, mid-session). Re-checking just now:
`rg-thinkschool-quotes-api` still exists as an empty shell with only a leftover Container Apps
managed environment (`cae-kjoiqlfbl4bpk`) in it — no container app, no App Insights component, no
Log Analytics workspace. So the piece is blocked again, but for a different, more mundane reason
than before: not "no subscription exists," but "the deployment that did exist was torn down before
this piece's verification finished." There is currently nothing running to query, and — same rule
as before — that means no screenshot and no "which endpoint surprised me" observation get invented
to paper over it.

What's unchanged and still correct, regardless of resource state:

- The exercise's KQL, checked against the real `requests` table schema (same schema [Day 4
  Piece 5](../../day-4/piece5)'s `queries.kql` already used).
- [`scripts/verify-and-save-function.sh`](scripts/verify-and-save-function.sh) — now not just
  written-but-untested, but actually run partway against a real deployment this session. Every
  command in it (`az monitor app-insights query`, `az monitor log-analytics workspace saved-search
  create`, including the `--func-alias`/`--func-param` flags) was checked against the real,
  installed CLI's own `--help` output.

Once [Day 5 Piece 4](../piece4)'s `azd up` is run again against the same (still-active)
subscription, this is genuinely one command away:

```bash
bash scripts/verify-and-save-function.sh                      # uses piece4's default names
# or, if your resource group / app name differ:
bash scripts/verify-and-save-function.sh <resource-group> <app-name>
```

No arguments are secrets — the resource group and app name are just names, not credentials. `az
login` (already run once for Piece 3/4) is the only prerequisite; the script discovers the App
Insights resource and its backing Log Analytics workspace by resource type rather than assuming a
name, since azd's Bicep names them from a hash (`appi-<resourceToken>`) that isn't knowable ahead
of a real deployment.

## The exercise's KQL, verbatim

```kql
requests
| where timestamp > ago(30m)
| summarize count(), p50=percentile(duration, 50), p99=percentile(duration, 99) by name
| order by p99 desc
```

This is valid KQL against the standard `requests` table every App Insights resource has — no
changes needed once a resource actually exists. `count()` with no arguments produces a column
named `Count` automatically; `percentile(duration, N)` on the `duration` column (already in
milliseconds in this table) is the standard idiom for "how slow is the slow tail," not just the
average.

## Saved as a function — [`queries.kql`](queries.kql)

```kql
let EndpointLatencySummary = (lookback:timespan=30m) {
    requests
    | where timestamp > ago(lookback)
    | summarize
        RequestCount = count(),
        p50 = percentile(duration, 50),
        p99 = percentile(duration, 99)
        by name
    | order by p99 desc
};
EndpointLatencySummary()
```

The Logs tab's "Save > Save as function" button does exactly this under the hood: it wraps
whatever query is in the editor in a named `let` binding scoped to the workspace. Two equally real
ways to actually create it, both ending at the same saved function:

**By hand in the portal:** paste the `let EndpointLatencySummary = ...` block into Logs, run it
once, click **Save → Save as function**, name it `EndpointLatencySummary`.

**Scripted** (what [`scripts/verify-and-save-function.sh`](scripts/verify-and-save-function.sh)
actually runs — real command, checked against the installed CLI):

```bash
az monitor log-analytics workspace saved-search create \
  --resource-group "$RESOURCE_GROUP" \
  --workspace-name "$WORKSPACE_NAME" \
  --name "EndpointLatencySummary" \
  --category "Performance" \
  --display-name "Endpoint Latency Summary" \
  --saved-query 'requests | where timestamp > ago(lookback) | summarize RequestCount = count(), p50 = percentile(duration, 50), p99 = percentile(duration, 99) by name | order by p99 desc' \
  --func-alias "EndpointLatencySummary" \
  --func-param "lookback:timespan = 30m"
```

Either way, from then on `EndpointLatencySummary()` (last 30 minutes) or `EndpointLatencySummary(1h)`
(custom window) both work from any query in that workspace — including from alert rules or
workbooks, which is the actual point of saving it rather than re-pasting the raw query each time.

## The observation

The exercise asks for one observation about which endpoint surprised me from the KQL result. I
still can't answer that with invented numbers — the deployment that did exist this session was
torn down before the query ever ran, so there's no `name` column with real rows to look at.
Guessing which endpoint "would probably" be slow and presenting that as an observation from real
data is exactly the kind of thing I won't do.

The one real, unscripted thing that did happen: the very first request this session sent to the
live app — `GET /health` — came back `504`, not `200`. Not a KQL observation (no query ran against
it), but a genuine surprise from hitting a real endpoint, which is closer to the spirit of the
exercise than nothing at all. See "What would break this" for why.

Once [Day 5 Piece 4](../piece4)'s `azd up` is run again against the still-active subscription, this
query is ready to run as-is against the redeployed `QuotesApi`, and the honest version of this
section gets written from whatever it actually shows.

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/main/day-5/piece5

(Not yet pushed — I don't commit or push without being asked. Ready for you to review, stage, and
push yourself.)

## Notes for mentor

The upstream blocker (no billable subscription) did eventually clear this session — the Amity
account's "Azure for Students" subscription came through, [Piece 4](../piece4)'s `azd up` succeeded
against it for real, and this piece's script got as far as one real endpoint hit before every
resource was deleted mid-verification. So the remaining gap here isn't "can this actually run" —
it's demonstrated that it can — it's "the deployment it needs didn't stay up long enough to finish
the query and the save-as-function step." Everything past that point (the KQL itself, the saved
function, the script) is real, correct, and ready to run unmodified the next time the app is
redeployed — nothing here needs to be rewritten, only re-executed against a live app.

## What did I learn this session?

"Save as function" isn't a special portal-only feature — it's KQL's own `let`-with-parameters
syntax, wrapped by a UI action that stores it against the workspace. Knowing that means a saved
function is something you can write, review, and version-control like any other query, rather than
a black box the portal manages for you.

## What would break this?

Percentiles need enough data points to mean anything — `percentile(duration, 99)` over a handful
of requests in a 30-minute window is really just "the slowest request," not a meaningful tail
latency figure; the exercise's `ago(30m)` window is fine for a demo hit a few times manually, but
misleading if read as production signal without enough real traffic behind it. Also, App Insights
ingestion has a delay (typically under 5 minutes but not instant) — hitting an endpoint and
immediately running this query against `ago(30m)` can legitimately show fewer requests than were
actually sent, which looks like a bug in the query when it's actually just ingestion lag.

One more, found while verifying this piece: `az monitor app-insights query` lives in the
`application-insights` CLI extension, which isn't installed by default. Run it in a script without
installing the extension first, and `az` tries to prompt "install it now? (Y/n)" — which throws an
unhandled `EOF when reading a line` traceback in any non-interactive shell (a script, CI, or this
session) instead of a clean error. `scripts/verify-and-save-function.sh` handles this itself —
`az extension add --name application-insights --upgrade --yes` runs first, idempotently — rather
than leaving it as a step someone has to remember to run first.

One more, found on a later correctness pass through this script (no live resource needed to catch
it): `az monitor app-insights query` takes `--apps`/`-a` (plural), not `--app`. The script had
`--app`, which happened to still work — `argparse` resolves it as an unambiguous prefix of `--apps`
since no other flag on that command starts with `--app` — but it's not the flag the command's own
`--help` actually documents, which undercuts the claim earlier in this README that every command
was checked against `--help` output. Fixed to `--apps` so the script matches the documented syntax
instead of relying on abbreviation matching that could break in a future CLI version. (Verified by
running the real query command against a nonexistent resource group: `--app` reached Azure and
failed with `ResourceGroupNotFound`, not an argument-parsing error — proof it was being accepted as
an abbreviation, not silently ignored.)

One more, found by actually hitting the real deployed app: **the very first request came back
`504`, not `200`.** Container Apps' consumption plan scales an idle app to zero replicas; the first
inbound request has to wait for a cold container start, and if that takes longer than the ingress
gateway's timeout, the caller sees a `504` even though the app is perfectly healthy a few seconds
later. A verification script that hits `/health` once, sees a non-200, and concludes the deployment
is broken would be wrong. `scripts/verify-and-save-function.sh` now retries `/health` up to 5 times
(10s apart) before treating the app as warmed up and moving on to the real hits that need to land
in `requests`; the alternative would be setting `minReplicas: 1` on the container app if cold
starts aren't acceptable for a given workload at all.
