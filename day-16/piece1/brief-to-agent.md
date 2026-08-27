# Brief to the agent (Claude Code)

**Target API (real, Week-1):** `QuotesApi` at
[thinkschool_Shagun_Yadav/day-1/piece3/QuotesApi](../../day-1/piece3/QuotesApi), run locally on
`http://localhost:5310` (`ASPNETCORE_URLS=http://localhost:5310 dotnet bin/Release/net10.0/QuotesApi.dll`).

- **List:** `GET /api/quotes/?page={page}&size={size}` - plain `[{id,author,text}]` array. Same contract
  day-15/piece1 already pinned; reuse `QuotesService.getQuotesPage(page, size)` as-is.
- **Detail:** `GET /api/quotes/{id}` - `200` with the quote, or `404` with an **empty body**. The
  endpoint is declared `MapGet("/{id:int}", ...)` (`QuoteEndpointExtensions.cs:43`) - the route
  constraint means a **non-numeric id never reaches the handler at all**. Confirmed live via curl,
  all three return `404` with an empty body, indistinguishably:
  ```
  curl http://localhost:5310/api/quotes/17      -> 200 {"id":17,"author":"Ada Lovelace",...}
  curl http://localhost:5310/api/quotes/999999  -> 404 (empty body) - genuinely missing id
  curl http://localhost:5310/api/quotes/abc     -> 404 (empty body) - route constraint rejects it
  curl http://localhost:5310/api/quotes/-1      -> 404 (empty body) - same
  ```
  **This is the id field the client has to guard, not the server:** the server cannot tell "id doesn't
  exist" apart from "that wasn't an id" - both collapse to the same 404. If the UI wants to say
  "that's not a valid id" instead of "not found," it has to validate the route param itself, before
  ever calling `getQuoteById`.

**Goal:** add client-side routing on top of the existing Day 15 Angular app
(`day-15/piece1/quotes-list-detail`, copied into this folder unmodified as the starting point, same
pattern day-15 used against day-14/piece2):

1. **A new "Routing" tab** alongside Explore / Create / Signal Forms / All Quotes / Interceptors,
   hosting a `<router-outlet>` for three routes: `login`, `quotes` (list), `quotes/:id` (detail).
   `provideRouter(routes, withViewTransitions(), withComponentInputBinding())` in `app.config.ts`.
2. **Lazy-loaded routes** - `login`, `quotes`, and `quotes/:id` each `loadComponent: () => import(...)`
   their own standalone component, so each is a separate build chunk fetched only when first navigated
   to (verifiable in the Network tab, not just asserted).
3. **A functional auth guard** on `quotes/:id` only (the list stays public; viewing one quote's detail
   requires being "logged in"). Since the real API has no auth of its own (day-15 already established
   this), back it with a small `AuthService` - a signal seeded from `localStorage['auth_token']`
   presence, with `login()`/`logout()` - and a `CanActivateFn` that returns `true` when authenticated or
   a `UrlTree` to `/login` otherwise (not a bare `false`, which blocks navigation but leaves the URL and
   screen wherever they were - a redirect has to be a `UrlTree`/`Router.navigate`, not just denial).
4. **The `quotes/:id` route param**, bound via `withComponentInputBinding()` so the component receives
   `id` as a plain string input - validate it's a positive integer *before* calling the API (per the
   note above), with three distinct explicit states beyond loading/found: invalid param, not-found
   (valid-looking id, real 404), and a generic error - `@switch`, no silent blank state.
5. **A View Transition between the quotes list and a quote detail** - give each list card and the
   detail's matching heading the same `view-transition-name` keyed by the quote's `id` (unique per
   card, so the browser doesn't collide two simultaneously-rendered elements on one name), so the
   morph targets the specific card that was clicked, not a generic cross-fade.

Do not modify `day-1/piece3/QuotesApi` or `day-15/piece1` - both are read-only reference / the
unmodified copy source, same rule day-15's brief used against day-14/piece2 and `day-1/piece3`.
