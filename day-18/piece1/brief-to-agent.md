# Brief to the agent (Claude Code)

**Exercise (Day 18 - Background jobs):** move slow work off the request thread. Implement a
`BackgroundService` that drains a queue, and contrast it with `IHostedService` and Hangfire for
scheduled work. Handle graceful shutdown via the cancellation token.

**Where:** `day-18/piece1`. Backend is [day-17/piece1](../../day-17/piece1)'s `QuotesApi`, copied
unmodified into [QuotesApi/](QuotesApi/) so the background-jobs feature could be added without
touching the read-only original. Frontend is day-17/piece1's Angular app
(`quotes-list-detail`), copied unmodified into [quotes-list-detail/](quotes-list-detail/) with one
new tab added on top: **Background Jobs**.

**What "the queue" is, concretely:** rather than a generic counter, the slow work is tied to the
app's actual domain - analyzing one quote (word count, character count, longest word) - so there's
a real reason to move it off the request thread and something meaningful to show in the UI while
it runs.

**Do not modify** `day-17/piece1` (or anything upstream of it) - read-only reference / copy
source, same rule every prior day has used. Anything needed from it was copied into
`day-18/piece1` first, never edited in place.

## The exercise's own gates

- Paste the `BackgroundService` and show how it shuts down cleanly - see
  [README.md](README.md) section 2 and the live shutdown log in
  [verification-log.md](verification-log.md) section 3.
- One line on when Hangfire beats a hosted service - see README.md section 3.
