# Verification log

Grounded in the real, running Week-1 `QuotesApi` (`day-1/piece3/QuotesApi`,
`ASPNETCORE_URLS=http://localhost:5310 dotnet bin/Release/net10.0/QuotesApi.dll`) and a real `ng serve`
dev build on port 4212, driven with headless-Chromium Playwright - unit tests first, then a live-browser
pass against the actual API, same two-layer approach as prior days.

## 1. Unit tests - `npm test`, 23/23 passing

```
 Test Files  4 passed (4)
      Tests  23 passed (23)
```

`quote-management-store.spec.ts` (7 tests) and 3 new `deleteQuote` tests appended to
`quotes.service.spec.ts` are new this session; the rest are day-16/piece1's untouched characterization
suite, still green.

## 2. Live-browser verification - Playwright + real `ng serve` + real API, 11/11 passing

```
Created throwaway quote id=38 for the delete test (real seed data left untouched).
PASS - Manage tab shows page 1 with 5 real quotes (page size = 5)
PASS - Real quote data rendered (Ada Lovelace among page 1 authors)
PASS - Next fetched GET /api/quotes/?page=2&size=5 (real request, params confirmed)
PASS - Page 2 shows the real remaining quotes (8 total incl. throwaway, size 5 -> 3 on page 2)
PASS - The throwaway test quote appears on page 2 (newest id sorts last)
PASS - Next is disabled on page 2 (partial page = hasNext heuristic correctly says no more)
PASS - Delete button disables itself ("Deleting...") the instant the first click fires
PASS - Double-click delete fires exactly ONE DELETE request for id=38 (deduped by deletingIds), not two
PASS - Deleted throwaway quote is gone from page 2 (2 real quotes remain there)
PASS - No error banner shown after a successful delete (even with a double-click)
PASS - Real seed quotes (7) are all still present after this run - only the throwaway quote was deleted

11/11 checks passed.
```

| State/edge exercised | How | Result |
|---|---|---|
| **Loading** | Fresh navigation to the Manage tab | `status.set('loading')` shown, then the real page-1 response resolves it |
| **Loaded (real data)** | Page 1 renders | 5 real quotes, author text matches the live API exactly |
| **Empty** | Unit test only - `page=3&size=5` on a 7-quote (then 8, incl. throwaway) dataset returns real `200 []` | `status` -> `'empty'`, distinct message. **Not reachable through this UI** - see "gap" below |
| **Error (invalid page)** | Unit test only - real `page=0` `ValidationProblemDetails` | `status` -> `'error'`, `friendlyMessage` = `"Page must be greater than 0."`. **Not reachable through this UI either** - Previous is disabled at page 1, so a user can never click their way to `page=0` |
| **Concurrent page navigation** | Unit test: page-2 response flushed before the earlier page-1 response, then the stale page-1 response is flushed | `switchMap` had already unsubscribed page 1 - `HttpTestingController` refuses to flush a cancelled request at all, proving it can never overwrite page 2 |
| **Concurrent delete (double-click)** | Live: two real clicks on the same Delete button, back-to-back, against a real (delayed) API response | Exactly **one** `DELETE` request went out; `deletingIds` blocked the second click synchronously |
| **Delete 404-as-success** | Unit test: a `DELETE` response flushed as `404` (id already gone) | Quote still removed from the list, `status` stays `'loaded'`/`'empty'`, **not** `'error'` |
| **Delete that's a real failure** | Unit test: a `DELETE` response flushed as `500` | `status` -> `'error'`, quote stays in the list (nothing removed on a genuine failure) |

## 3. The concrete bug caught (and fixed) this session

**Wrong assumption going in:** the first draft's `deleteQuote(id)` had no guard at all - `error: (err) =>
{ this.error.set(err); this.status.set('error'); }` for *any* failed delete.

**What actually happened:** a genuine double-click race, reproduced first in a unit test
(`quote-management-store.spec.ts`, "a double-click delete... must NOT surface an error"), before any UI
existed. Two calls to `deleteQuote(17)` fired two real `DELETE /api/quotes/17` requests. The real API's
first `DELETE` succeeds (`204`); the second - for an id that's already gone - returns `404` (confirmed
live via curl against a throwaway quote, id 33: `204` then `404`, not guessed). The naive handler mapped
that second response straight to `status.set('error')`, so a delete that **worked** produced a visible
error banner.

**Fix:** two parts, both grounded in the real endpoint's actual behavior -
1. `deletingIds`, a `Set<number>` of in-flight delete ids. `deleteQuote` checks it first and returns
   immediately if the id is already being deleted, so a second click never even fires a second request.
2. Even so, a second `DELETE` for the same id can still happen from outside this button (a second tab, a
   retry) - so `err.status === 404` is treated as "already gone" (success), not a failure, in
   `settleDelete`.

Re-ran the test - passes. Re-verified live in the browser (`Delete button disables itself... the instant
the first click fires` + `Double-click delete fires exactly ONE DELETE request`, both PASS above).

## 4. A second real gap this session surfaced (documented, not silently papered over)

`hasNext` is a heuristic (`quotes().length === PAGE_SIZE`) because the real API returns no total count.
It happens to be correct for this dataset (page 2 has 2 items, a partial page, so `hasNext` correctly
reads `false`) - but it means a user can **never reach the true "empty" state through this UI**: if the
real data were an exact multiple of `PAGE_SIZE` (e.g. exactly 10 quotes at size 5), page 2 would be a
full page, `hasNext` would say `true`, Next would be enabled, and only *then* would clicking it hit a
real `page=3&size=5` -> `200 []`. With today's 7 (then 8, including the throwaway) real quotes, that
exact alignment doesn't occur, so `'empty'` and the invalid-page `'error'` state are both proven correct
against the real API contract in the unit tests, but neither is currently reachable by clicking through
the live UI. Worth flagging to a reviewer rather than claiming full UI coverage.

## 5. An incident this session, disclosed in full

An earlier draft of the Playwright verification script selected `.quote-row` (`.first()`) - "the first
quote on the page" - as the delete target, rather than a quote created specifically for the test. That
script ran three times in a row while I was diagnosing an unrelated timing question (see the "Deleting..."
disabled-state check above), and each run's "first row" was, by then, a **different real seeded quote**
(ids 17 "Ada Lovelace", then 18 and 19, both "Grace Hopper" - the two quotes ahead of it on the list
after each prior deletion). All three were permanently removed from the real local dev database
(`day-1/piece3/QuotesApi/quotes.db`, SQLite, confirmed via its `ConnectionStrings` config) - this API has
no seed/reset step in its code, so this was not self-healing.

**Caught by:** re-querying `GET /api/quotes/?page=1&size=100` after the fact and noticing only 4 of the
original 7 real quotes remained.

**Fixed by:** re-creating all three via `POST /api/quotes/` with their exact original author/text
(confirmed against the page-1/page-2 responses captured earlier in this same session, before the
deletions). The **content** is restored exactly; the **ids** are not (34/35/36 instead of 17/18/19,
since the API auto-increments and has no way to reuse a freed id) - anything that hardcoded those
specific ids against this local dataset would need to be updated. Final state re-confirmed live: 7 real
quotes present, content matching the originals.

**Root cause and the actual fix, not just a patch:** the verification script was rewritten to `POST` a
disposable, clearly-labelled quote (`author: 'PW Verify Throwaway'`) at the start of every run and
target *only* that quote by matching its text, never "whatever happens to be first." The final
verification pass (section 2 above) explicitly re-checks `GET /api/quotes/` afterward and asserts all 7
real quotes are still present and only the throwaway id is gone - that assertion is now a permanent part
of the script, not a one-time manual check.

**Why this belongs in the log, not swept into "lessons learned":** this is the real-world version of
exactly the risk this exercise is about - a destructive endpoint (`DELETE`), state that looks
disposable in a test script but is backed by a real persistent store, and no confirmation step in
between. It happened during *my own* verification tooling, not the store's application code, but the
store's code is exactly what made the mistake possible to make safely correctable elsewhere: nothing in
`QuoteManagementStore` itself talks to `quotes.db` directly, so the fix was confined to the test script,
not a code change.

## 6. What would break this

- **The API's pagination contract changes** - e.g. a total count gets added to the response, or the
  plain-array shape becomes `{ items: [...], total: N }`. `hasNext`'s heuristic (`length === PAGE_SIZE`)
  would need to be replaced with the real total; until then it silently mis-predicts "one more page"
  exists on any dataset that happens to end on an exact multiple of `PAGE_SIZE` (see gap #4).
- **The `id` field is renamed or retyped on `Quote`** (`models/quote.model.ts`) - `deleteQuote(id: number)`
  and `filter((q) => q.id !== id)` both assume `id` exists and is a `number`; a rename would silently stop
  removing rows from the local `quotes` signal after a successful server-side delete (the DELETE would
  still succeed - the row just wouldn't disappear from the screen until the next full page reload).
- **The DELETE endpoint's status codes change** - e.g. it starts returning `200` with a body instead of
  `204`, or a `409 Conflict` instead of `404` for "already gone." `settleDelete`'s `err.status === 404`
  check is the one place this assumption is hardcoded; a different "already deleted" status would route
  straight into the `'error'` branch instead of being treated as success.
- **A second feature starts mutating the same underlying quote data** - see the NgRx/signal-store
  threshold note in the README: `QuotesStore` (Explore/Create/All Quotes/Interceptors tabs) and this
  session's `QuoteManagementStore` (Manage tab) each hold their own independent copy of quote data with
  no synchronization between them. Deleting a quote in Manage does **not** remove it from `QuotesStore`'s
  `quotes` signal - `QuotesStore` only self-heals on its own 8-second poll (`quotes-store.ts:70`,
  `HEALTH_CHECK_INTERVAL_MS`), so for up to 8 seconds, Explore/All Quotes can show a quote that no longer
  exists, and clicking into its detail (`/quotes/:id` from day-16/piece1's routing) would 404 for a quote
  the list still displays as present. This is the concrete, code-level version of "why you'd eventually
  want one shared store instead of two."

## Running it

```bash
cd day-16/piece2/quotes-list-detail
npm install
npm test              # 23/23, no server needed
npm start -- --port 4212
```

Proxies `/api/*` to `http://localhost:5310` (`proxy.conf.json`) - start `day-1/piece3/QuotesApi` on that
port first:
```bash
cd day-1/piece3/QuotesApi
ASPNETCORE_URLS=http://localhost:5310 dotnet bin/Release/net10.0/QuotesApi.dll
```

Open the app, click the **Manage** tab. Delete only quotes you don't mind losing from the local dev
database - there is no undo and no reseed step.
