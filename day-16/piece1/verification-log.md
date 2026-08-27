# Verification log

Grounded in the real, running Week-1 `QuotesApi` (`day-1/piece3/QuotesApi`,
`ASPNETCORE_URLS=http://localhost:5310 dotnet bin/Release/net10.0/QuotesApi.dll`) and a real `ng serve`
dev build on port 4211, driven with headless-Chromium Playwright - same two-layer approach as day-15:
unit tests first, then a live-browser pass against the actual API.

## 1. Unit tests - `npm test`, 12/12 passing

```
 Test Files  3 passed (3)
      Tests  12 passed (12)
   Duration  4.44s
```

The two new tests (`core/auth.guard.spec.ts`) pin the guard's actual contract - `true` when
authenticated, and specifically a `UrlTree` to `/login` (not a bare `false`) when it isn't. The other
10 are day-15's untouched characterization suite, still green.

## 2. Live-browser verification - Playwright + real `ng serve` + real API, 12/12 passing

```
PASS - Routing tab shows the public quotes list by default - cards=7
PASS - Extracted a real quote id from the list - id=17
PASS - Clicking a card while logged out redirects to /login (guard returns a UrlTree, not a bare false)
PASS - The guard blocks BEFORE the detail chunk is fetched (redirected user never downloads it)
PASS - After logging in, redirected to /quotes
PASS - Authenticated navigation to /quotes/:id succeeds (no redirect to /login)
PASS - The detail route chunk was fetched lazily on the authenticated navigation
PASS - Quote detail content rendered for the valid id
PASS - Reloading directly on /quotes/:id still renders the quote detail (not the Explore tab)
PASS - Non-numeric id shows the client-side "invalid id" state (no API call)
PASS - Nonexistent numeric id shows the "not found" (real 404) state, distinct from "invalid"
PASS - After logging out, the same id redirects to /login again

12/12 checks passed.
```

| State/edge exercised | How | Result | Screenshot |
|---|---|---|---|
| **Guard pass** | Logged in (`AuthService.login()`), navigated to `/quotes/17` | Detail rendered, no redirect | `6-detail-authenticated.png` |
| **Guard redirect** | Logged out, clicked a real quote card | Sent to `/login`, exact `UrlTree` from `authGuard` | `2-guard-redirect-to-login.png` |
| **Lazy chunk in the Network tab** | Watched request events for `quote-detail-route` while logged out (blocked) vs. authenticated (allowed) | `0` requests when the guard redirects first; `chunk-364NO4DU.js` + the component's HMR request fetched only once actually authenticated and navigated | (build output + captured request log, below) |
| **Missing/invalid route param - invalid** | `/quotes/abc`, authenticated | `"abc" isn't a valid quote id.` - no `GET /api/quotes/abc` request at all | `4-invalid-id-no-api-call.png` |
| **Missing/invalid route param - not found** | `/quotes/999999`, authenticated | `No quote with id 999999.` - real `404`, empty body | `5-not-found-real-404.png` |
| **Guarded route blocks a malformed id too** | `/quotes/abc`, logged out | Still redirected to `/login` - the guard runs before the id is ever validated | `3-guarded-route-blocks-even-a-malformed-id.png` |
| **Public list** | `/quotes`, logged out | All 7 real quotes render, no auth required | `1-list-public-logged-out.png` |

Lazy-loading, confirmed two ways:

1. **The production build output** names each route's own chunk explicitly:
   ```
   Lazy chunk files    | Names              |  Raw size
   chunk-364NO4DU.js   | quote-detail-route |  12.01 kB |
   chunk-4TGUXLYL.js   | quotes-list-route  |  11.05 kB |
   chunk-RSV2LSKE.js   | login-route        |   5.27 kB |
   ```
2. **Live request capture**, not just the build log:
   - While logged out, clicking a card and getting redirected produced **zero** requests matching
     `quote-detail-route` - the guard blocks the navigation before Angular ever loads the lazy chunk,
     so a user who can't see the detail also never downloads its code.
   - Once authenticated, the same navigation produced the chunk request:
     `GET /chunk-364NO4DU.js` and `GET /@ng/component?c=...quote-detail-route.ts@QuoteDetailRoute`.

## 3. The concrete bug caught (and fixed) this session

**Wrong assumption going in:** `App`'s `activeTab` signal, which decides whether the router-outlet
(nested inside the "Routing" tab) is even in the DOM, could be initialized from `inject(Router).url` -
"the router already knows the current URL by the time my component constructs."

**What actually happened:** a Playwright reload test - `page.goto('/quotes/17')` then `page.reload()` -
came back showing the **Explore tab**, not the quote. Not a rendering bug in the detail component
itself (that part worked fine on in-app navigation); `Router.url` was simply still `'/'` at the moment
`App`'s constructor ran, because Angular's initial navigation is asynchronous and hadn't resolved yet.
The signal captured that stale value once, at construction, and never revisited it.

**Fix:** read `location.pathname` instead - the real, synchronous browser URL - to decide the initial
tab:
```ts
protected readonly activeTab = signal<Tab>(
  location.pathname.startsWith('/quotes') || location.pathname.startsWith('/login') ? 'routing' : 'explore'
);
```
Re-ran the same reload test - the quote detail renders correctly on a fresh load, confirmed in
`6-detail-authenticated.png`'s scenario extended with an explicit `page.reload()` check
(`"Reloading directly on /quotes/:id still renders the quote detail"` in the log above).

**Why this matters beyond the test:** this is exactly the kind of thing that "works" in every manual
click-through (you're never testing a cold reload when you're the one clicking Explore -> Routing ->
a card) and only breaks on a real deep link or a page refresh - the scenario a mentor/reviewer bookmarks
or shares a URL for.

## 4. A second real gap this session surfaced (documented, not silently papered over)

Screenshotting the "invalid id" and "not found" states while logged out first produced the **login
screen** for both, not the states themselves - because `authGuard` is registered on the whole
`quotes/:id` route, so it runs before the component (and its client-side id-format check) ever mounts.
This is consistent with the design (the list is public, the detail is not), but it means: **the
"invalid id" and "not found" messages are only ever visible to an authenticated user.** An unauthenticated
visitor typing `/quotes/abc` gets the exact same `/login` redirect as one typing a real id - confirmed in
`3-guarded-route-blocks-even-a-malformed-id.png`.

## 5. What would break this

- **The real API's `{id:int}` route constraint changes** (e.g. `QuoteEndpointExtensions.cs:43`'s
  `MapGet("/{id:int}", ...)` becomes `MapGet("/{id}", ...)` with manual parsing that returns a
  `400 ValidationProblemDetails` for a bad id instead of a plain 404): `QuoteDetailRoute`'s client-side
  `VALID_ID_PATTERN` check would still correctly block obviously-malformed ids before calling the API,
  but any id shape the server newly rejects that the client's regex still accepts (e.g. a huge number
  the server now flags as out of range) would surface as a generic `'error'` state instead of a
  specific one, since nothing here branches on a 400 for this endpoint the way `getQuotesPage` does.
- **The `id` field changes type or name on `Quote`** (`models/quote.model.ts`): `getQuoteById(Number(rawId))`
  doesn't validate the response shape at runtime - a rename would silently render `undefined` in the
  detail view, the same failure mode day-13/14/15's logs already documented for this API's other
  fields.
- **`authGuard`'s placeholder auth** - like day-15's `authInterceptor`, there's no real backend to
  reject a bad token; `AuthService.isAuthenticated()` is a pure client-side flag. Anyone can flip it
  from the browser console (`localStorage.setItem('auth_token', 'x')`) with no server ever checking it.
  This guard demonstrates the *routing* mechanism (redirect-when-unauthenticated), not real
  authorization - a genuine auth system would need the server to reject the request too, not just the
  client to hide the button.
- **A future route added under `quotes-routing/`** that forgets `loadComponent` (uses eager `component:`
  instead) silently loses its lazy-loading - nothing here enforces that every route in `quotes.routes.ts`
  stays lazy; a reviewer has to actually check the Network tab per route, same as this session did,
  rather than trust the pattern of the two that already exist.

## Running it

```bash
cd day-16/piece1/quotes-list-detail
npm install
npm test              # 12/12, no server needed
npm start -- --port 4211
```

Proxies `/api/*` to `http://localhost:5310` (`proxy.conf.json`) - start
`day-1/piece3/QuotesApi` on that port first:
```bash
cd day-1/piece3/QuotesApi
ASPNETCORE_URLS=http://localhost:5310 dotnet bin/Release/net10.0/QuotesApi.dll
```

Open the app, click the **Routing** tab, click a card while logged out to see the guard redirect, then
**Log in** and click it again.
