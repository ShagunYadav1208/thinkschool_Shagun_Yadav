# Verification log

## 1. Azure provisioning

All via `az` CLI, under the `Azure for Students` subscription, reusing the same region
(`eastasia`) as day-17's resources for consistency:

```
az group create --name syquotes19-rg --location eastasia

az servicebus namespace create --resource-group syquotes19-rg --name syquotes19sb \
  --location eastasia --sku Standard

az servicebus topic create --resource-group syquotes19-rg --namespace-name syquotes19sb \
  --name quote-events

az servicebus topic subscription create --resource-group syquotes19-rg --namespace-name syquotes19sb \
  --topic-name quote-events --name audit-log --max-delivery-count 5 --lock-duration PT30S

az servicebus topic subscription create --resource-group syquotes19-rg --namespace-name syquotes19sb \
  --topic-name quote-events --name notifications --max-delivery-count 3 --lock-duration PT15S

az role assignment create --role "Azure Service Bus Data Owner" \
  --assignee-object-id <signed-in user's object id> --assignee-principal-type User \
  --scope /subscriptions/<id>/resourceGroups/syquotes19-rg/providers/Microsoft.ServiceBus/namespaces/syquotes19sb
```

Confirmed the topic's `RequiresDuplicateDetection` is `False` (the default, left off deliberately -
see README.md section 1) directly from the `az servicebus topic create` output.

**Gotcha hit:** the `az role assignment create --scope "/subscriptions/..."` command failed with
`ERROR: (MissingSubscription) The request did not have a subscription or a valid tenant level
resource provider` on the first attempt. Root cause: Git Bash (MSYS) auto-converts any argument
starting with `/` into a Windows path before `az` ever sees it, mangling the scope string. Fixed by
prefixing the command with `MSYS_NO_PATHCONV=1`.

**RBAC propagation:** the role assignment succeeded immediately (confirmed via `az role assignment
create`'s own output), but the app's first connection attempt still needed a few minutes to reflect
it in practice - normal Azure AD propagation delay, not an error, and it had already resolved by
the time the backend code was finished and first tested (see section 2).

## 2. A real bug caught: `DefaultAzureCredential` doesn't fall through to `AzureCliCredential` here

First attempt used `new DefaultAzureCredential()` unconditionally. Result, live, on the very first
quote creation:

```
fail: QuotesApi.ServiceBusMessaging.AuditLogProcessorService[0]
      audit-worker-1 error on quote-events/Subscriptions/audit-log
      Azure.Identity.AuthenticationFailedException: ManagedIdentityCredential authentication failed:
      All Managed Identity sources are unavailable. The Azure Instance Metadata Service (IMDS) ...
      IMDSv2 probe failed ... Retry failed after 5 tries ...
```

The exception propagated all the way up through `DefaultAzureCredential.GetTokenFromSourcesAsync` -
it never attempted `AzureCliCredential` at all, even though the local `az login` session was valid
(confirmed separately: `az account show` succeeded throughout). Fixed by selecting the credential
explicitly by environment in `InfrastructureExtensions.AddInfrastructure` (now takes an
`IHostEnvironment` parameter): `new AzureCliCredential()` in Development, `new
DefaultAzureCredential()` otherwise - mirroring the pattern day-17 used for choosing between SQLite
and Azure SQL by connection-string shape rather than trusting a single code path to auto-detect
correctly everywhere.

After the fix, clean startup:

```
info: QuotesApi.ServiceBusMessaging.AuditLogProcessorService[0]
      audit-worker-1 started, competing for messages on subscription audit-log.
info: QuotesApi.ServiceBusMessaging.AuditLogProcessorService[0]
      audit-worker-2 started, competing for messages on subscription audit-log.
info: QuotesApi.ServiceBusMessaging.NotificationProcessorService[0]
      Notifications processor started on subscription notifications.
```

## 3. Live publish -> both subscriptions -> idempotent dedupe

```
$ curl -X POST http://localhost:5921/api/quotes/ -d '{"author":"Rumi","text":"The wound is the place where the light enters you."}'
{"id":1,"author":"Rumi","text":"..."}

$ curl http://localhost:5921/api/servicebus/audit-log
[{"messageId":"e9be9f60-...","quoteId":1,"author":"Rumi","handledBy":"audit-worker-2","wasDuplicate":false, ...}]

$ curl http://localhost:5921/api/servicebus/notifications
[{"messageId":"e9be9f60-...","quoteId":1,"message":"New quote by Rumi: \"...\"","wasDuplicate":false, ...}]

# Replay - same message id, simulating a publisher retry
$ curl -X POST http://localhost:5921/api/servicebus/replay/1
{"eventId":"e9be9f60-...", ...}

$ curl http://localhost:5921/api/servicebus/audit-log
[
  {"messageId":"e9be9f60-...","handledBy":"audit-worker-1","wasDuplicate":true, ...},
  {"messageId":"e9be9f60-...","handledBy":"audit-worker-2","wasDuplicate":false, ...}
]
```

Two things confirmed by this one sequence: **competing consumers** (the replay landed on
`audit-worker-1`, a *different* worker than the original `audit-worker-2`), and **cross-worker
idempotency** (worker-1 still correctly recognized the message id as already-handled, because
`ProcessedMessageTracker` is one shared instance for the whole `AuditLogProcessorService`, not one
per worker).

## 4. Live poison message -> DLQ

```
$ curl -X POST http://localhost:5921/api/servicebus/poison
{"eventId":"481c4fd6-...","quoteId":-1,"author":"Poison Test","text":"POISON: ...", ...}
```

Application log, three delivery attempts over ~45s (3 x 15s lock duration on the `notifications`
subscription):

```
warn: Poison event 481c4fd6-... (delivery attempt 1) - throwing deliberately.
warn: Poison event 481c4fd6-... (delivery attempt 2) - throwing deliberately.
warn: Poison event 481c4fd6-... (delivery attempt 3) - throwing deliberately.
```

```
$ curl http://localhost:5921/api/servicebus/dlq
[{
  "messageId": "481c4fd6-...",
  "body": "{\"EventId\":\"481c4fd6-...\",\"QuoteId\":-1,\"Author\":\"Poison Test\", ...}",
  "deliveryCount": 3,
  "deadLetterReason": "MaxDeliveryCountExceeded",
  "deadLetterErrorDescription": "Message could not be consumed after 3 delivery attempts.",
  ...
}]
```

## 5. Graceful shutdown of both new BackgroundServices

Same verification technique as Day 18 (a temporary `POST /api/test/shutdown` endpoint calling
`IHostApplicationLifetime.StopApplication()`, added, exercised, then removed - this sandboxed
environment can't deliver a real console Ctrl+C/SIGTERM to a backgrounded process). Full shutdown
log:

```
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
info: QuotesApi.ServiceBusMessaging.NotificationProcessorService[0]
      Stopping notifications processor, letting any in-flight handler finish.
info: QuotesApi.ServiceBusMessaging.AuditLogProcessorService[0]
      Stopping audit-log processors, letting any in-flight handler finish.
info: QuotesApi.BackgroundJobs.QueuedHostedService[0]
      Graceful shutdown requested; giving the in-flight job (if any) time to unwind.
info: QuotesApi.BackgroundJobs.QueuedHostedService[0]
      Queued hosted service stopping - drain loop exited.
```

All three hosted services (Day 18's job queue plus both new Service Bus processors) stopped
cleanly, no unhandled exceptions, no hung shutdown.

## 6. Backend build, frontend build/tests

```
dotnet build   -> Build succeeded, 0 errors (2 pre-existing NU1903 advisory warnings, unrelated)
npm install    -> 372 packages, clean
ng build       -> Application bundle generation complete, no errors
ng test        -> Test Files 4 passed (4), Tests 23 passed (23) - existing suite, untouched
```

## 7. Live in an actual browser

`ng serve` (isolated port, temporary proxy config pointed at an isolated test API instance) driven
with Playwright's Chromium:

- Loaded the app, clicked **Service Bus**, filled author/text, clicked **Create + publish**.
  Screenshot: [screenshots/service-bus-after-publish.png](screenshots/service-bus-after-publish.png).
- Clicked **Replay last event (same message id)**. Audit log and notifications both grew to two
  rows for the same message id - one `No`, one `Yes (skipped)` - matching section 3's direct-API
  result exactly. Screenshot:
  [screenshots/service-bus-after-replay.png](screenshots/service-bus-after-replay.png).
- Clicked **Send poison message**, waited for the DLQ table to populate (~45s). Final state showed
  *two* dead-lettered messages - the one from section 4's direct-API test plus this new one from
  the browser, both `MaxDeliveryCountExceeded`, both `deliveryCount: 3`. Screenshot:
  [screenshots/service-bus-dlq.png](screenshots/service-bus-dlq.png).
- Console error listeners (`pageerror` + `console.error`): **empty** across the whole run.

One retake was needed: the first screenshot attempt's wait condition (`rows.length > 0` on the DLQ
table) was already satisfied by the pre-existing poison message from section 4 before the new one
had time to land, so it captured only one row. Verified directly via `curl` that the second message
had, in fact, also landed (`b7b69a4b-...`, `deliveryCount: 3`), then re-ran with a corrected wait
condition (`rows.length >= 2`) to get the final two-row screenshot.

## 8. Deployment onto day-17's existing infrastructure

```
# Grant the App Service's own managed identity access to the new namespace
az webapp identity show -n syquotes17-api -g syquotes17-rg   # -> principalId
az role assignment create --role "Azure Service Bus Data Owner" \
  --assignee-object-id <principalId> --assignee-principal-type ServicePrincipal \
  --scope /subscriptions/<id>/resourceGroups/syquotes19-rg/.../namespaces/syquotes19sb

# Backend
dotnet publish -c Release -o publish
# Repack as a Unix-flagged zip (day-17 verification-log.md documents why - Kudu's Linux
# zip extractor mangles Windows-flagged entries from System.IO.Compression)
python3 zip_unix.py publish api.zip
az webapp deploy --resource-group syquotes17-rg --name syquotes17-api --src-path api.zip --type zip

# Frontend
ng build --configuration production
npx @azure/static-web-apps-cli deploy ./dist/quotes-list-detail/browser \
  --deployment-token <token> --env production
```

Both deploys succeeded cleanly on the first attempt (no repeat of day-17's Kudu zip bug or
SqlClient version bug - the Unix-zip fix and the pinned `Microsoft.Data.SqlClient` version both
carried forward unmodified from day-17/piece1). Confirmed via `az webapp log deployment show`:
`"Deployment successful. deployer = OneDeploy"`.

**Live smoke test immediately after deploy:**

```
$ curl https://syquotes17-api.azurewebsites.net/api/quotes/?page=1&size=3
[{"id":2,"author":"Shagun Yadav","text":"Working"}]     # pre-existing data, untouched

$ curl -X POST https://syquotes17-api.azurewebsites.net/api/quotes/ -d '{"author":"Live Test","text":"deployed and verified live on day 19"}'
{"id":4, ...}

$ curl https://syquotes17-api.azurewebsites.net/api/servicebus/audit-log
[{"messageId":"db979f81-...","handledBy":"audit-worker-2","wasDuplicate":false, ...}]
```

Confirms the deployed App Service reached the real Service Bus namespace via its managed identity
(RBAC had already propagated by the time this ran), and that `DefaultAzureCredential` (the
non-Development branch of the credential selection in `InfrastructureExtensions`) works correctly
in a real App Service, unlike the local dev environment (section 2).

## 9. A deployment-only bug: both new tabs silently empty on the live site

First browser check of the live site's new tabs (Playwright, `page.goto` the real SWA URL) showed
**"No events yet." / "No jobs yet."** on both Background Jobs and Service Bus, despite section 8's
direct `curl` calls proving the API had real data. No console error, no failed network request in
Playwright's `requestfailed`/`response` listeners - the request "succeeded."

Root cause, found by fetching the exact same relative path a service was calling, directly in the
live page's own context:

```js
await fetch('/api/servicebus/audit-log')
// -> { ok: true, status: 200, text: "<!doctype html>...<app-root></app-root>..." }
```

`/api/servicebus/audit-log` (relative) resolved against the **Static Web App's own origin**, not
the API - the SWA's SPA navigation fallback served `index.html` for that unmatched route, as a real
`200 text/html` response. `HttpClient` fails to parse HTML as JSON, and both `JobsService` and
`ServiceBusService`'s polling code catches that failure with an empty handler (`error: () => {}`) -
deliberately, so a transient failure during a 2-second poll doesn't flash an error banner - which
is exactly what made this silent instead of an obvious crash.

Both services had copied `QuotesService`'s relative-path pattern (`apiBaseUrl: '/api/quotes/'`)
without copying the piece that makes it work in production: `environment.prod.ts` overrides that
one constant to the App Service's absolute URL specifically because there's no dev-server proxy
once the app is static files on a CDN. Neither new service had an equivalent override.

**Fix:** added `apiOrigin` to both environment files (`''` in dev - an empty prefix in front of a
relative path is a no-op, so the dev-proxy path is unaffected; the real App Service origin in
prod), rewired both services' `baseUrl` to `` `${environment.apiOrigin}/api/...` ``.

**Re-verified live after the fix** (screenshots in [screenshots/](screenshots/)):

- [screenshots/live-service-bus.png](screenshots/live-service-bus.png) - real audit log,
  notifications, and both DLQ entries (from local testing earlier in this session, since the DLQ is
  the same real Service Bus subscription regardless of which QuotesApi instance queries it).
- [screenshots/live-background-jobs.png](screenshots/live-background-jobs.png) - a real
  quote-analysis job, enqueued and completed against the live deployed API.
- [screenshots/live-explore.png](screenshots/live-explore.png) - baseline: existing tabs/data
  unaffected by any of this.
- Console error listeners: empty on every live check, before and after the fix (the bug was never a
  JS exception - that's precisely what made it easy to miss without testing the live site itself).

## Side effects avoided

Learned from Day 18: every test run used an isolated API port (never the frontend's committed
`proxy.conf.json` target), and `proxy.conf.json` itself was never edited - a temporary proxy config
file was passed to `ng serve --proxy-config` instead. No pre-existing process or committed config
was touched.
