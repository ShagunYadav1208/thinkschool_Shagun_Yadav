# Day 16 / Piece 2 — State management, signals first

Built on top of [day-16/piece1](../piece1)'s Angular app (routing/guards), copied unmodified into
[quotes-list-detail/](quotes-list-detail/) in this folder (piece1 itself is untouched) - same
copy-not-edit rule every prior day in this series has used against its predecessor.

The deliverable is the direct-and-verify loop: the brief given to the agent, the agent's resulting
signals-first store, and a verification log grounded in the real, running Week-1 API and a real headless
browser - not narrated, not assumed. **This session's log also includes a real incident** (an earlier
verification script accidentally deleted three real seeded quotes from the local dev database) and how
it was caught, fixed, and prevented from recurring - see
[verification-log.md](verification-log.md) section 5.

## 1. The brief

Full text in [brief-to-agent.md](brief-to-agent.md). The real API detail it hinges on:

> **Target API (real, Week-1):** `QuotesApi` at `day-1/piece3/QuotesApi`
> - `GET /api/quotes/?page={page}&size={size}` - plain array, no total count. `page >= 1`, `size` in
>   `[1,100]`, else `400 ValidationProblemDetails`.
> - `DELETE /api/quotes/{id}` - unused until this session. `204` on success, `404` (empty body) if
>   already gone - confirmed live: delete a real id, delete it again, get `404`.
>
> **Goal:** a `QuoteManagementStore` (plain signals + a root-provided service, no NgRx) modeling a
> paginated, deletable quote list against those two endpoints, with four explicit states
> (`loading`/`error`/`empty`/`loaded`) and two kinds of concurrent-update handling: a page-navigation
> race (`switchMap` cancels stale requests) and a delete race (double-click dedup + treating a 404 on
> delete as "already gone," not a failure). Plus, separately: my own judgment call on when this would
> stop being enough and NgRx/`@ngrx/signals` would be worth it.

## 2. The agent's output

**`src/app/quotes.service.ts`** gained one method, grounded in a live curl check (create a throwaway
quote, delete it, delete it again):

```typescript
/**
 * DELETE /api/quotes/{id} - 204 no content on success, or 404 (empty body)
 * if the id doesn't exist - confirmed live: created a throwaway quote,
 * deleted it (204), deleted the same id again (404). The second DELETE of
 * an id that's already gone is NOT a 204 - callers that fire two deletes
 * for the same id (e.g. a double-click) will see the second one reject.
 */
deleteQuote(id: number): Observable<void> {
  return this.http.delete<void>(`${environment.apiBaseUrl}${id}`);
}
```

**`src/app/quote-management-store.ts`** - the actual deliverable:

```typescript
export type PageStatus = 'loading' | 'error' | 'empty' | 'loaded';
const PAGE_SIZE = 5;

@Injectable({ providedIn: 'root' })
export class QuoteManagementStore {
  readonly page = signal(1);
  readonly quotes = signal<Quote[]>([]);
  readonly status = signal<PageStatus>('loading');
  readonly error = signal<AppHttpError | null>(null);
  readonly hasPrevious = computed(() => this.page() > 1);
  // No total count from the API - a full page is the only signal more might exist.
  readonly hasNext = computed(() => this.quotes().length === PAGE_SIZE);
  readonly deletingIds = signal<ReadonlySet<number>>(new Set());

  private readonly fetch$ = new Subject<number>();

  start(): void {
    // switchMap cancels the PREVIOUS page request the instant a newer page
    // is requested - rapid next/previous clicks can't land out of order.
    this.fetch$.pipe(
      switchMap((page) => this.quotesService.getQuotesPage(page, PAGE_SIZE).pipe(
        map((quotes) => ({ ok: true as const, quotes })),
        catchError((err: AppHttpError) => of({ ok: false as const, err }))
      ))
    ).subscribe((result) => {
      if (result.ok) {
        this.quotes.set(result.quotes);
        this.status.set(result.quotes.length === 0 ? 'empty' : 'loaded');
      } else {
        this.error.set(result.err);
        this.status.set('error');
      }
    });
    this.goToPage(1);
  }

  deleteQuote(id: number): void {
    if (this.deletingIds().has(id)) return; // dedupe a double-click before it fires a 2nd request
    this.deletingIds.update((ids) => new Set(ids).add(id));
    this.quotesService.deleteQuote(id).subscribe({
      next: () => this.settleDelete(id, true),
      error: (err: AppHttpError) => this.settleDelete(id, err.status === 404, err), // 404 = already gone = success
    });
  }
  // ...settleDelete removes the id from `quotes`, or sets 'error' for a real failure.
}
```

Full file: [quote-management-store.ts](quotes-list-detail/src/app/quote-management-store.ts).

**New tab:** [quote-management-view/](quotes-list-detail/src/app/quote-management-view/) - a paginated
list with a per-row Delete button (disabled while in flight), wired into
[app.ts](quotes-list-detail/src/app/app.ts) / [app.html](quotes-list-detail/src/app/app.html) as a
seventh "Manage" tab.

### The NgRx / signal-store threshold - my call, not the agent's

I'd move `QuoteManagementStore` off a plain signals-and-a-service pattern and onto `@ngrx/signals`
(`signalStore`) or full NgRx once **any one** of these becomes true - none of them are true yet:

1. **More than one store needs to agree on the same entity.** Right now `QuotesStore` (Explore/Create/All
   Quotes/Interceptors) and `QuoteManagementStore` (Manage) each hold an independent copy of quote data.
   Deleting a quote in Manage doesn't remove it from `QuotesStore` - it only self-heals on `QuotesStore`'s
   own 8-second poll (`quotes-store.ts:70`). At 2 stores that's a documented gap; at 4-5 stores all
   touching the same entity, it turns into "which one is stale right now" bugs a plain service can't
   answer without a shared, normalized store and explicit invalidation.
2. **The async flow gets too tangled to test by calling methods and asserting on signals.** Every store
   in this app so far is one HTTP resource with a linear fetch -> loading/error/empty/loaded shape. NgRx
   earns its ceremony once a single user action needs to orchestrate several dependent requests with
   real branching, not just "fetch, then render."
3. **Debugging needs devtools time-travel / an action log**, not just a debugger stepping through a
   subscribe callback - useful once state bugs stop being reproducible from a fresh page load.
4. **Real normalization is needed** - e.g. a "favorites" feature that references quotes by id across
   multiple lists, where a plain `Quote[]` per feature starts requiring the same dedup/join logic
   NgRx's entity adapters exist to solve.

For a single paginated CRUD-ish view against one REST resource, a signal + `Subject`/`switchMap` service
is less code, easier to read top-to-bottom, and doesn't need a store library's mental model. #1 above is
the one already visible in this actual codebase (not hypothetical) - see
[verification-log.md](verification-log.md) section 6 for the concrete reproduction.

## 3. Verification log

Full detail in **[verification-log.md](verification-log.md)** - summary:

- **23/23 unit tests pass** (`npm test`) - piece1's 20 untouched, plus 7 new ones for
  `QuoteManagementStore` and 3 new ones pinning `deleteQuote`'s real 204/404 contract.
- **11/11 live-browser checks pass** - real `ng serve`, headless Chromium via Playwright, against the
  real API: pagination against real data, a genuine double-click delete race, the delete-disabled state
  made observable under an artificial response delay, and a final re-check that the real 7 seeded quotes
  are all still present.
- **The concrete bug caught:** the first draft's `deleteQuote` treated *any* delete failure as an error.
  A real double-click fires two `DELETE /api/quotes/{id}` requests; the real API's second one 404s (the
  id is already gone, confirmed live via curl on a throwaway id) - so a delete that **worked** was
  showing an error banner. Fixed with an in-flight `deletingIds` guard plus treating a 404-on-delete as
  success.
- **An incident, disclosed in full:** an earlier draft of my own verification script targeted "the first
  quote row" instead of a purpose-made throwaway quote, and across three diagnostic runs it permanently
  deleted three real seeded quotes (ids 17, 18, 19) from the local dev SQLite database. Caught by
  re-querying the API, fixed by recreating them with identical content (new ids - the API doesn't reuse
  freed ones), and the script was rewritten to always create and target a disposable quote, with a final
  assertion that the real dataset is untouched. Full account in verification-log.md section 5.
- **What would break this:** the pagination contract changing shape (no total count today - `hasNext` is
  a heuristic), an `id` field rename on `Quote`, the DELETE endpoint's status codes changing, and the
  cross-store staleness described in the NgRx threshold section above.

## Running it

```bash
cd day-16/piece2/quotes-list-detail
npm install
npm test              # 23/23, no server needed
npm start -- --port 4212
```

Proxies `/api/*` to `http://localhost:5310` (`proxy.conf.json`) - start
`day-1/piece3/QuotesApi` on that port first:
```bash
cd day-1/piece3/QuotesApi
ASPNETCORE_URLS=http://localhost:5310 dotnet bin/Release/net10.0/QuotesApi.dll
```

## GitHub link

To be pushed to the `thinkbridge-thinkschool` org - link to follow once pushed.

## Notes for mentor

- `day-1/piece3/QuotesApi`, `day-15/piece1`, and `day-16/piece1` were read-only reference / copy source;
  no source files there were modified. **The one exception is data, not code:** see below.
- **Please read verification-log.md section 5.** While diagnosing an unrelated UI-timing question during
  live verification, an earlier draft of my Playwright script deleted three real seeded quotes (ids 17
  "Ada Lovelace", 18 and 19 "Grace Hopper") from `day-1/piece3/QuotesApi/quotes.db` via the real DELETE
  endpoint. I caught it by re-querying the API, restored the same author/text via `POST` (new ids -
  34/35/36 - since the database doesn't reuse freed ones), and rewrote the verification script so all
  destructive testing from here on targets only a quote the script itself creates. I'm flagging this
  explicitly rather than only fixing it quietly, since it's exactly the kind of state-mutation risk this
  exercise is about, and any earlier day's material that hardcoded ids 17/18/19 against this local
  dataset will now see different ids for the same content.
- `node_modules/` and `.angular/` were excluded from the `day-16/piece1` -> `day-16/piece2` copy (robocopy
  `/XD`) and reinstalled with `npm install` rather than copied wholesale, since piece1's copy of them was
  itself already excluded from git.
- `deleteQuote` guards against a double-click at the store level (`deletingIds`), not just by disabling
  the button in the template - the guard holds even if a second click somehow reaches the handler.

## What did I learn this session?

The double-click delete bug was easy to predict in the abstract ("two clicks, two requests") but the
*specific* shape of the fix only became obvious after checking what the real API actually does with a
repeat delete - a 404, not a second 204. If I'd guessed instead of curling it, I likely would have
written a generic "ignore delete errors while deletingIds still had this id" fix, which would have
silently swallowed a real 500 too. Checking the real contract first is what made "treat 404 specifically
as success, everything else as a real error" the actual fix instead of a broader, wrong one.

The incident in section 5 taught the sharper lesson: a destructive endpoint against a real (if local)
persistent store deserves the same caution in *my own test tooling* as in application code. "It's just a
test script" isn't true when the script's target is real data with no reset step.

## What would break this

See [verification-log.md](verification-log.md) section 6 - the pagination contract's missing total
count, an `id` rename on `Quote`, the DELETE endpoint's status codes changing, and the cross-store
staleness between `QuotesStore` and `QuoteManagementStore` that's the concrete case for the NgRx
threshold above.
