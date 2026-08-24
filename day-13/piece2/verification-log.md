# Verification log

Everything below is from an actual run against the real `QuotesApi` (Week-1, `day-1/piece3/QuotesApi`,
`http://localhost:5310`) and the real Angular app (`ng serve --port 4201`), driven by a headless
Chromium via Playwright. The race-condition test uses Playwright's network interception
(`page.route`) to add a real, deterministic delay to one specific HTTP response - not a `setTimeout`
sprinkled into the app's own code - so the interleaving is genuine, not simulated inside the app.

## States and edges exercised

| # | State | How it was produced | Result |
|---|---|---|---|
| 1 | Initial - nothing selected | Fresh page load, before any click | `"Select a quote to see its detail."` - [screenshot](verification-screenshots/1-initial-state.png) |
| 2 | Detail loads normally | Clicked the first quote | Correct quote text + author rendered - [screenshot](verification-screenshots/2-detail-loaded.png) |
| 3 | **Stale-response race** | See below - the main event | Fixed: final state always matches the *last* click, regardless of response order |
| 4 | Detail 404 | `page.route` returned a real `404` with an empty body for one quote's detail request | `"Quote not found."` - [screenshot](verification-screenshots/4-detail-404-not-found.png) |
| 5 | List error | `page.route` returned `500` for `GET /api/quotes/` | `"Couldn't load quotes."` - [screenshot](verification-screenshots/5-list-error.png) |
| 6 | List empty | `page.route` returned `200 []` for `GET /api/quotes/` | `"No quotes yet."` - [screenshot](verification-screenshots/6-list-empty.png) |

## The race condition: reproduced as a real bug, then fixed and re-verified

**Setup:** `page.route('**/api/quotes/17', ...)` delays *only* quote 17's detail response by 3
seconds; every other request (including quote 19's) resolves immediately. Sequence: click quote 17
(Ada Lovelace - slow), wait 200ms, click quote 19 (Grace Hopper, "A ship in port..." - fast).

**First draft** (`selectQuote()` called `.subscribe()` directly on every click, no cancellation):

```
3a. After the fast click (id=19) resolves: detail shows the ship quote (correct so far)
3b. After id=17's delayed response finally arrives, detail pane shows:
    "That brain of mine is something more than merely mortal." | STILL CORRECT: false
```

Quote 19 was still selected (highlighted) in the list, but the detail pane had been silently
overwritten back to quote 17's text - the exact stale-response bug this exercise asked to catch.
[Screenshot of the actual bug](verification-screenshots/3b-race-BEFORE-fix-BUG.png) - captured by
briefly reverting the component back to the naive `.subscribe()`-per-click version specifically to
get real photographic evidence of the failure, not just a console log claiming it happened, then
restoring the `switchMap` fix afterward (confirmed identical to the pre-revert version, and confirmed
still building clean).

**Fix:** route every click through a `Subject<number>` piped through `switchMap`, not a direct
`.subscribe()` per click:

```typescript
private readonly select$ = new Subject<number>();

// in ngOnInit():
this.select$.pipe(
  switchMap((id) => this.quotesService.getQuoteById(id).pipe(
    map((quote) => ({ ok: true as const, quote })),
    catchError(() => of({ ok: false as const }))
  )),
  takeUntilDestroyed(this.destroyRef)
).subscribe((result) => { /* ... */ });

protected selectQuote(id: number): void {
  this.selectedId.set(id);
  this.detailLoading.set(true);
  this.select$.next(id);
}
```

`switchMap` unsubscribes the *previous* inner observable the instant a new value arrives on `select$`
- quote 17's in-flight HTTP request is cancelled (or its late response is simply never routed to
`.subscribe()`) the moment quote 19 is clicked. Re-ran the exact same delayed-response test after the
fix:

```
3a. After the fast click (id=19) resolves: detail shows the ship quote (correct so far)
3b. After id=17's delayed response finally arrives, detail pane shows:
    "A ship in port is safe, but that is not what ships are built for." | STILL CORRECT: true
```

[Screenshot of the fixed state](verification-screenshots/3b-race-after-stale-response-FIXED.png) -
same interleaving, quote 19 still highlighted in the list and still showing in the detail pane after
quote 17's late response arrives. Compare directly against the buggy screenshot above - same click
sequence, same delay, different final result.

`catchError` deliberately lives *inside* `switchMap`'s own pipe, not wrapped around the whole chain -
an uncaught error on one detail request would otherwise terminate the `select$` subscription entirely,
and every click after the first 404 would silently stop doing anything.

## What would break if the API contract changed

- **The 404-with-empty-body detail matters more than it looks.** The first draft's error handler
  never inspected the response body (`error: () => this.detailError.set(true)`), so this one happened
  to be safe by construction - but a version that tried to read `err.error.message` to show a
  friendlier message would get `undefined` today, and a `TypeError` if the API's error body ever
  became a plain string instead of JSON or `null`.
- **A field rename** (`text` -> `quoteText`, say) breaks the detail pane silently - `quote.text` in
  the template just renders nothing, no compile error, since `HttpClient.get<Quote>()` doesn't
  validate the response against the type parameter at runtime.
- **Changing the list order** (e.g. newest-first instead of insertion order) wouldn't break anything
  here - nothing in this component depends on list order, only on `id`, which is exactly why the race
  fix keys off the id passed to `switchMap`, not an index or array position.
