# Brief to the agent (Claude Code)

**Target API (real, Week-1):** `QuotesApi` at
[thinkschool_Shagun_Yadav/day-1/piece3/QuotesApi](../../day-1/piece3/QuotesApi), run locally on
`http://localhost:5310` (`ASPNETCORE_URLS=http://localhost:5310 dotnet bin/Release/net10.0/QuotesApi.dll`).

- **Paginated list:** `GET /api/quotes/?page={page}&size={size}` - plain `[{id,author,text}]` array, no
  total count. `page` must be `>= 1`, `size` in `[1, 100]`, or a `400 ValidationProblemDetails`.
  Confirmed live: `page=1&size=5` on the real 7-quote dataset returns 5 (ids 17,18,19,22,26), `page=2&size=5`
  returns the remaining 2 (ids 31,32), `page=999&size=5` returns `200 []` (empty, not a 404).
- **Delete:** `GET /api/quotes/{id}` route also has `DELETE /api/quotes/{id}` (`QuoteEndpointExtensions.cs:96`)
  - unused by any existing tab. `204 No Content` on success, `404` (empty body) if the id doesn't exist.
  Confirmed live: created a throwaway quote via `POST`, deleted it once (`204`), deleted the *same id
  again* (`404` - the id genuinely is gone, this isn't a guess). **A double-click on a delete button
  will fire this exact "delete an already-deleted id" case if nothing guards against it.**

**Goal:** model a small, real feature's state with **signals + a plain `@Injectable({providedIn:'root'})`
service** - no NgRx, no `@ngrx/signals` - against the endpoints above, on top of the existing Day 16
Angular app (`day-16/piece1/quotes-list-detail`, copied unmodified into this folder as the starting
point, same pattern day-16/piece1 used against day-15/piece1):

1. **`QuoteManagementStore`** (`quote-management-store.ts`, root-provided, next to the existing
   `QuotesStore`): paginated browsing (`page`, `quotes`, `hasNext`/`hasPrevious` computed signals) plus
   per-row delete, backed by `QuotesService.getQuotesPage`/`deleteQuote` (the latter is new - add it).
2. **Four explicit states**, `PageStatus = 'loading' | 'error' | 'empty' | 'loaded'` - no silent blank
   state. `loading` while a page fetch is in flight, `error` on a real `4xx`/`5xx` (e.g. an invalid
   `page`), `empty` for a real `200 []` page beyond the data, `loaded` otherwise.
3. **Concurrent updates, two distinct kinds:**
   - **Page navigation race** - rapid next/previous clicks before an earlier page request resolves.
     `switchMap` over a `Subject<number>` cancels the stale request so an older, slower response can
     never overwrite a newer page.
   - **Delete race** - a double-click firing two `DELETE`s for the same id. Since the real API's second
     `DELETE` of an already-gone id 404s (not another `204`), the store has to treat that specific 404 as
     "already deleted" (success), not a failure - and should short-circuit the second click before it
     even fires a second request.
4. **A new "Manage" tab** (`quote-management-view/`) wired into `app.ts`/`app.html` alongside the
   existing six, purely to exercise the store: a page of quotes, a disabled-while-in-flight Delete
   button per row, Previous/Next.
5. **The judgment call is mine, not the agent's:** in the README, state - in my own words, grounded in
   this actual codebase - the threshold at which I'd move this from a plain signal service to
   `@ngrx/signals`/NgRx.

Do not modify `day-1/piece3/QuotesApi`, `day-15/piece1`, or `day-16/piece1` - all three are read-only
reference / unmodified copy source, same rule prior days used. **Any file needed from `day-16/piece1`
gets copied into `day-16/piece2` first, never edited in place.** Given the target API is a shared local
dev database (`day-1/piece3/QuotesApi/quotes.db`), delete-endpoint testing must run against
throwaway quotes created for that purpose - never against the real seeded rows.
