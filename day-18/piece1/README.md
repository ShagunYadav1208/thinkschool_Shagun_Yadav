# Day 18 / Piece 1 — Background jobs

Backend is [day-17/piece1](../../day-17/piece1)'s `QuotesApi`, copied unmodified into
[QuotesApi/](QuotesApi/) so a background-jobs feature could be added without touching the
read-only original. Frontend is day-17/piece1's Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/) with one new tab added: **Background Jobs**.

## Current status

**Built, tested, and verified live** - backend `dotnet build` clean; a real enqueue-to-completion
round trip over HTTP; a real mid-job graceful-shutdown captured in a log (section 3 below); frontend
`ng build` + the existing 23/23 test suite unaffected; the new tab exercised in an actual headless
Chromium browser against a live dev server + API, screenshot confirmed, zero console errors.

Not pushed to GitHub yet - see "GitHub link" below.

## 1. What was built

The slow work is tied to the app's actual domain rather than a generic counter: analyzing one
quote (word count, character count, longest word) takes a simulated ~5 seconds. That's real enough
to be worth moving off the request thread, and gives the UI something meaningful to show while a
job runs.

- **[QuotesApi/BackgroundJobs/](QuotesApi/BackgroundJobs/)**
  - `IBackgroundTaskQueue` / `BackgroundTaskQueue` - a bounded `System.Threading.Channels.Channel`
    holding `Func<IServiceProvider, CancellationToken, Task>` work items. Bounded (capacity 100,
    `FullMode.Wait`) so a burst of enqueues applies backpressure instead of growing memory without
    limit.
  - `IJobStore` / `JobStore` - a `ConcurrentDictionary<Guid, JobRecord>`, process-lifetime only.
    Both the request thread (creating/reading jobs) and the drain loop (mutating status) touch it
    concurrently.
  - `QuoteAnalysisJob` - the actual work item factory: fetches the
    quote, then five `Task.Delay(1s, cancellationToken)` steps (chunked, not one 5s delay, so the
    token is observed mid-flight the way a real multi-step operation would check it), then computes
    and stores the result.
  - `QueuedHostedService` - the `BackgroundService` that drains the queue. Full listing and
    shutdown behavior in section 2.
- **[QuotesApi/Extensions/JobEndpointExtensions.cs](QuotesApi/Extensions/JobEndpointExtensions.cs)** -
  `POST /api/jobs/quote-analysis/{quoteId}` (202 Accepted, returns immediately with the queued
  `JobRecord`), `GET /api/jobs/` (all jobs, newest first), `GET /api/jobs/{id}`.
- **[QuotesApi/Extensions/InfrastructureExtensions.cs](QuotesApi/Extensions/InfrastructureExtensions.cs)** -
  registers the queue and job store as singletons, `QueuedHostedService` via
  `AddHostedService`, and sets `HostOptions.ShutdownTimeout = 10s` (the grace window a running job
  gets on shutdown before the host tears the process down regardless).
- **[quotes-list-detail/src/app/background-jobs-view/](quotes-list-detail/src/app/background-jobs-view/)** -
  new tab: a quote-id input + "Enqueue analysis" button, and a table (polling `GET /api/jobs/`
  every second) showing each job's status badge and result/error. `jobs.service.ts` and
  `models/job.model.ts` are new; `app.ts`/`app.html` gained the tab, nothing else in the copied app
  was touched.

**Screenshots** (from the live browser run in verification-log.md section 5):

- [screenshots/background-jobs-running.png](screenshots/background-jobs-running.png) - right after
  enqueueing, button reads "Queuing...".
- [screenshots/background-jobs-completed.png](screenshots/background-jobs-completed.png) - same
  job a few seconds later, status badge updated to Completed with the real computed result.

## 2. The `BackgroundService` and its graceful shutdown

```csharp
public class QueuedHostedService(
    IBackgroundTaskQueue taskQueue,
    IServiceScopeFactory scopeFactory,
    ILogger<QueuedHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Queued hosted service starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Func<IServiceProvider, CancellationToken, Task> workItem;

            try
            {
                workItem = await taskQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using var scope = scopeFactory.CreateScope();

            try
            {
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("A queued job was cancelled by application shutdown before it finished.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception executing a queued background job.");
            }
        }

        logger.LogInformation("Queued hosted service stopping - drain loop exited.");
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Graceful shutdown requested; giving the in-flight job (if any) time to unwind.");
        return base.StopAsync(cancellationToken);
    }
}
```

**How the shutdown actually unfolds**, confirmed live (full log in verification-log.md section 3):
on Ctrl+C/SIGTERM the host cancels `stoppingToken` and calls `StopAsync`, which - via the base
`BackgroundService` - awaits `ExecuteAsync`'s task for up to `HostOptions.ShutdownTimeout` (10s,
configured in `InfrastructureExtensions`) before the process is torn down regardless. Two cases:

- **Waiting on the queue, nothing running:** `DequeueAsync`'s `ReadAsync(stoppingToken)` throws
  `OperationCanceledException` immediately, the loop breaks, nothing is lost except whatever was
  never dequeued (the queue is in-memory, not persisted - it doesn't survive the process anyway).
- **A job already running:** it receives the *same* `stoppingToken`, so a job written to check it
  (`Task.Delay(1s, cancellationToken)` per step, in `QuoteAnalysisJob`) unwinds itself within the
  10s grace window - marks its own `JobRecord` `Cancelled` with a reason, then rethrows - instead of
  being killed mid-write. A job that ignored the token and ran past the grace window would still get
  torn down when the timeout expires; the token is cooperative, not a hard kill switch.

One bad or cancelled job never takes the loop itself down - the `catch (Exception ex)` around a
single work item means every other queued job still gets its turn, and the loop only exits when
`stoppingToken` says shutdown is happening.

## 3. `BackgroundService` vs `IHostedService` vs Hangfire

`BackgroundService` is an abstract base class that already implements `IHostedService` - it hands
you one `ExecuteAsync(CancellationToken)` loop and does the `StartAsync`/`StopAsync` plumbing
around it. `IHostedService` is the raw interface underneath; you'd implement it directly only when
the shape doesn't fit "one continuous loop" (e.g. you need `StartAsync` to kick off several
independent timers rather than run a single loop, or you need finer control over what `StopAsync`
does before the loop is even cancelled). For "drain a queue," `BackgroundService` is the
right-sized default here.

**One line on Hangfire:** reach for Hangfire instead of a plain `BackgroundService` queue the
moment a job needs to survive a restart, run on a schedule (cron/recurring), retry with backoff, be
inspected from a dashboard, or run across more than one instance - none of which this in-memory
`Channel` + `ConcurrentDictionary` gives you: the queue and every `JobRecord` here are lost the
instant the process restarts, and there's exactly one consumer, in this one process.

## What did I learn this session?

The cancellation token isn't a kill switch by itself - it's the *host* that turns cancelling the
token into an actual grace period (`HostOptions.ShutdownTimeout`) before force-tearing the process
down. Writing the job body to observe the token in small steps (`Task.Delay(1s, token)` x5 instead
of one `Task.Delay(5s, token)`) is what actually makes "graceful" visible: the difference between a
job that gets to mark itself `Cancelled` cleanly mid-flight versus one that's still sitting at
"Running" forever because it only ever checked the token once, at the very start.

## What would break this

- **The queue is in-memory.** A crash (not a graceful shutdown - an unhandled exception outside the
  per-work-item `catch`, or `kill -9`) loses every job still sitting in the channel, queued or
  running, with no record of it ever having existed. This is the exact gap Hangfire's persisted
  storage closes.
- **Single consumer, single process.** Scaling `QuotesApi` to two instances behind a load balancer
  gives each instance its own queue and its own `JobStore` - a job enqueued via instance A never
  gets picked up or become visible if the browser's next poll lands on instance B.
- **A job that ignores the cancellation token entirely** (e.g. a tight CPU-bound loop with no
  `await` inside it) can't be interrupted by `stoppingToken` at all - it would run past the 10s
  `ShutdownTimeout` and get torn down mid-work regardless of how "graceful" the surrounding
  plumbing is. Cooperative cancellation only works if the work actually cooperates.
- **No idempotency check on re-enqueue.** Nothing stops the same `quoteId` from being queued twice
  concurrently - two `JobRecord`s, two independent 5-second runs, same result computed twice. Not
  wrong, just wasteful; a real job queue serving production traffic would usually dedupe.

## GitHub link

Not pushed yet. Remote for the `thinkbridge-thinkschool` org is already configured locally as
`thinkschool` (per day-17's README); link to follow once pushed.

## Notes for mentor

- `day-17/piece1` (and everything upstream of it) was read-only reference / copy source - nothing
  there was modified. Any file needed from it was copied into `day-18/piece1` first.
- Full verification detail, including the live shutdown log, is in
  [verification-log.md](verification-log.md).
- The copied `quotes-list-detail`'s `proxy.conf.json` points at `http://localhost:5310`, but the
  copied `QuotesApi`'s `launchSettings.json` defaults to `http://localhost:5116` - that mismatch
  already existed in day-17/piece1 unmodified (confirmed by diffing the two), not something
  introduced here. Left as-is since fixing it is out of scope for this exercise; noted here so it
  isn't mistaken for a Day 18 regression.
