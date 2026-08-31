# Verification log

## 1. Backend builds clean

```
dotnet build   -> Build succeeded, 0 errors (2 pre-existing NU1903 SQLitePCLRaw advisory warnings,
                   unrelated to this change - present in day-17/piece1 unmodified)
```

## 2. Live enqueue -> completion round trip

Ran the copied `QuotesApi` locally on an isolated port, created a quote, then drove the new
endpoints directly over HTTP:

```
$ curl -X POST http://localhost:5919/api/quotes/ -d '{"author":"Test","text":"one two three four five six seven"}'
{"id":1,"author":"Test","text":"one two three four five six seven"}

$ curl -X POST http://localhost:5919/api/jobs/quote-analysis/1
{"id":"6752fa22-...","type":"quote-analysis","input":"1","status":1, ...}   # 1 = Running, picked up immediately

# ~5s later
$ curl http://localhost:5919/api/jobs/
[{"id":"6752fa22-...","status":2,"result":"7 words, 33 characters, longest word \"three\".", ...}]  # 2 = Completed
```

Confirms: enqueue returns immediately (202, no 5-second block on the request thread), the queue is
picked up without polling delay, and the computed result is correct for the input text.

## 3. Live graceful shutdown, mid-job

To trigger the exact shutdown path a real Ctrl+C/SIGTERM takes
(`IHostApplicationLifetime.StopApplication()` -> host cancels `stoppingToken` -> `StopAsync`)
without a real attached console (the sandboxed dev environment used here can't deliver a console
control event to a backgrounded process), a temporary `POST /api/test/shutdown` endpoint calling
`lifetime.StopApplication()` was added, exercised, and then **removed** before this was called done
- it was never part of the deliverable, only a way to drive the identical internal shutdown path
this environment couldn't reach externally.

Enqueued a job, waited 2 seconds into its ~5-second run (confirmed via a fresh `SELECT` log line
timestamped mid-job), then triggered shutdown. Full log:

```
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (1ms) [...] SELECT "q"."Id", "q"."Author", "q"."Text" FROM "Quotes" ...
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
info: QuotesApi.BackgroundJobs.QueuedHostedService[0]
      Graceful shutdown requested; giving the in-flight job (if any) time to unwind.
warn: QuotesApi.BackgroundJobs.QueuedHostedService[0]
      A queued job was cancelled by application shutdown before it finished.
info: QuotesApi.BackgroundJobs.QueuedHostedService[0]
      Queued hosted service stopping - drain loop exited.
```

This is exactly the code path documented in README.md section 2: the in-flight job's
`Task.Delay(1s, stoppingToken)` observed the cancellation, `QuoteAnalysisJob`'s catch block marked
the job `Cancelled` and rethrew, `QueuedHostedService` caught the rethrow and logged the warning,
and the drain loop exited cleanly - no unhandled exception, no crash, and the whole sequence
completed in well under the 10s `HostOptions.ShutdownTimeout` budget.

A first attempt at this test used real elapsed wall-clock time between separate tool invocations to
space out "enqueue" and "trigger shutdown," and the ~5s job had already completed by the time
shutdown was triggered (nothing to cancel) - the log above is from a corrected single atomic
command that enqueues and shuts down within a tight, measured 2-second window.

## 4. Frontend builds, tests pass

```
npm install      -> 372 packages added, clean
ng build         -> Application bundle generation complete, no errors
ng test          -> Test Files 4 passed (4), Tests 23 passed (23) - the existing suite, untouched by this change
```

## 5. Live in an actual browser

`ng serve` (isolated port) + the isolated `QuotesApi` test instance from section 2/3 (proxy
temporarily repointed at the test instance's port, restored to the original `5310` target
afterward - restoring the proxy under this file's own history closes the loop cleanly). Drove it
with Playwright's Chromium (already installed on this machine) rather than a manual click-through:

- Loaded the app, clicked the **Background Jobs** tab, filled quote id `1`, clicked
  **Enqueue analysis**.
- Screenshot immediately after enqueueing: job row present with a `Queuing...` button state.
- Waited for the status badge to read `Completed` (polling every second, as designed): job row
  updated in place to `Completed`, result `7 words, 33 characters, longest word "three".` - matches
  section 2's direct-API result for the same input, confirming the frontend renders exactly what
  the API returns, not a hardcoded string.
- `console --errors` equivalent (Playwright's `pageerror`/`console.error` listeners): **empty** -
  no runtime errors during the whole enqueue-to-completion flow.

Screenshots saved to [screenshots/](screenshots/):
[background-jobs-running.png](screenshots/background-jobs-running.png) and
[background-jobs-completed.png](screenshots/background-jobs-completed.png).

## A real bug caught during this session

The first draft of `background-jobs-view.html` wrote the literal path
`/api/jobs/quote-analysis/{quoteId}` directly in template text (inside a `<code>` tag) to describe
the endpoint. Angular's template compiler treats a bare `{...}` in text content as the start of an
ICU expansion message (used for `{count, plural, ...}`-style i18n), and a literal `{quoteId}` with
no matching ICU grammar broke parsing - not just at that line, but cascaded into unrelated
`NG5002` errors at the *next* interpolation in the file, which was initially confusing since the
reported error location didn't point at the real cause. The existing `quote-management-view.html`
(copied in from day-17, untouched) had already hit and worked around the same issue - it renders
`DELETE /api/quotes/{id}` as `&#123;id&#125;` - which is the fix applied here too.

## Side effect caught and undone

An early test accidentally pointed at port `5310`, which turned out to already be occupied by a
QuotesApi instance running outside this session (not started by this work) - `dotnet run` on that
port failed to bind and the subsequent `curl` calls silently hit the *other* server instead,
creating one throwaway quote ("Marie Curie ...") in its database. Caught immediately by checking
`netstat` after an unexpectedly-populated response (`id: 40` on what should have been a fresh
database), and undone with a `DELETE` against that same quote id before any further testing.
All actual verification in sections 2-5 above was re-run against a fully isolated port instead.
