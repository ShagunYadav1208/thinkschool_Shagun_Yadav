# Day 16 / Piece 1 — Routing, lazy loading, guards

Built on top of [day-15/piece1](../../day-15/piece1)'s Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/) in this folder (day-15 itself is untouched) - same pattern
day-15 used against day-14/piece2, and day-14 used against day-13/piece2.

Like the prior days, the deliverable is the direct-and-verify loop itself: the brief given to the
agent, the agent's resulting code, and a verification log grounded in the real, running Week-1 API and
a real headless browser - not narrated, not assumed.

## 1. The brief

Full text in [brief-to-agent.md](brief-to-agent.md). The real API detail it hinges on:

> **Target API (real, Week-1):** `QuotesApi` at `day-1/piece3/QuotesApi`
> - `GET /api/quotes/{id}` - `200` with the quote, or `404` with an **empty body**. The route is
>   declared `MapGet("/{id:int}", ...)` - confirmed live via curl, a non-numeric id (`/api/quotes/abc`)
>   returns the exact same `404` empty body as a genuinely missing id (`/api/quotes/999999`). The
>   server can't tell them apart; the client has to validate the id itself if it wants to say
>   something more useful than "not found."
>
> **Goal:** a new "Routing" tab with three lazy-loaded routes (`login`, `quotes`, `quotes/:id`), a
> functional `authGuard` on `quotes/:id` only (the list stays public), the `:id` route param bound via
> `withComponentInputBinding()` and validated client-side against the API's own `{id:int}` shape, and
> a View Transition (`withViewTransitions()`) between a list card and its detail, keyed by the quote's
> real `id`.

## 2. The agent's output

**`src/app/core/auth.service.ts`** and **`auth.guard.ts`** - a client-only "logged in" flag backed by
the same `auth_token` localStorage key `authInterceptor` already reads (day-15), and a `CanActivateFn`
that returns a `UrlTree` to `/login` (not a bare `false`) when unauthenticated:

```typescript
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);
  return authService.isAuthenticated() ? true : router.createUrlTree(['/login']);
};
```

**`src/app/quotes-routing/quotes.routes.ts`** - three lazy-loaded routes, only the detail one guarded:

```typescript
export const quotesRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'quotes' },
  { path: 'login', loadComponent: () => import('./login-route/login-route').then((m) => m.LoginRoute) },
  { path: 'quotes', loadComponent: () => import('./quotes-list-route/quotes-list-route').then((m) => m.QuotesListRoute) },
  {
    path: 'quotes/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./quote-detail-route/quote-detail-route').then((m) => m.QuoteDetailRoute),
  },
];
```

**`src/app/quotes-routing/quote-detail-route/quote-detail-route.ts`** - the route param, bound via
`withComponentInputBinding()`, validated against the real API's `{id:int}` constraint *before* ever
calling it:

```typescript
const VALID_ID_PATTERN = /^[1-9]\d*$/;

export class QuoteDetailRoute {
  readonly id = input<string>();
  protected readonly status = signal<DetailStatus>('loading'); // 'invalid' | 'not-found' | 'error' | 'found'

  constructor() {
    effect(() => {
      const rawId = this.id();
      if (!rawId || !VALID_ID_PATTERN.test(rawId)) {
        this.status.set('invalid');
        return;
      }
      this.quotesService.getQuoteById(Number(rawId)).subscribe({
        next: (quote) => { this.quote.set(quote); this.status.set('found'); },
        error: (err: AppHttpError) => { this.error.set(err); this.status.set(err.status === 404 ? 'not-found' : 'error'); },
      });
    });
  }
}
```

**`src/app/app.config.ts`** gained the router:

```typescript
provideRouter(quotesRoutes, withComponentInputBinding(), withViewTransitions()),
```

**View Transitions:** each list card's title and the matching detail heading share a
`view-transition-name` keyed by the quote's real id (`'quote-title-' + quote.id`), so the browser's
native morph targets the specific card that was clicked, not a generic cross-fade.

**New tab:** [routing-view/](quotes-list-detail/src/app/routing-view/) - a `<router-outlet>` plus a
logged-in/out indicator and a Log out button, wired into
[app.ts](quotes-list-detail/src/app/app.ts) / [app.html](quotes-list-detail/src/app/app.html) as a
sixth "Routing" tab alongside Explore / Create / Signal Forms / All Quotes / Interceptors.

## 3. Verification log

Full detail, states/edges table, and screenshots in **[verification-log.md](verification-log.md)** -
summary:

- **12/12 unit tests pass** (`npm test`) - day-15's 10 untouched, plus 2 new ones pinning `authGuard`'s
  `UrlTree`-not-`false` redirect contract.
- **12/12 live-browser checks pass** - a real `ng serve` build, headless Chromium via Playwright,
  against the real API on `localhost:5310`: guard pass, guard redirect, the detail chunk fetched
  lazily only on the authenticated navigation (zero requests when the guard blocks first), a
  reload-on-deep-link check, and both the invalid-id and real-404 states rendering distinct messages.
- **The concrete bug caught:** `App`'s initial tab was read from `inject(Router).url`, which is still
  `'/'` at construction time - the router's initial navigation is asynchronous. A reload on
  `/quotes/17` silently rendered the Explore tab instead of the quote. Fixed by reading
  `location.pathname` instead (the real, synchronous browser URL). Full mechanism and the fix in the
  log.
- **A second gap surfaced, not just the one required:** `authGuard` runs before `QuoteDetailRoute`'s
  own id-format validation ever gets a chance to - so `/quotes/abc` while logged out redirects to
  `/login` exactly like a valid id would, and the "invalid id" message is only ever reachable once
  authenticated.
- **What would break the contract:** a change to the API's `{id:int}` route constraint, an `id`
  field rename on `Quote`, and the client-only nature of `authGuard` (no server-side check backs it) -
  full detail in the log.

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

## GitHub link

To be pushed to the `thinkbridge-thinkschool` org - link to follow once pushed (see Notes for mentor).

## Notes for mentor

- `day-1/piece3/QuotesApi` and `day-15/piece1` were read-only reference for the contract / copy
  source; no source files there were modified.
- `node_modules/` and `.angular/` were copied wholesale from day-15/piece1 rather than reinstalled, to
  avoid a redundant network install of an already-resolved `package-lock.json`; both remain gitignored.
- `authGuard` guards `quotes/:id` only, not `quotes` - a deliberate choice to demonstrate a real
  permission boundary (browse publicly, view one detail once "logged in") rather than gating the whole
  feature.

## What did I learn this session?

A functional guard returning a bare `false` "works" in the sense that the wrong screen never renders -
but nothing about it says a redirect happened, because nothing did. The router just stops. Returning a
`UrlTree` is what turns "blocked" into "redirected," and it's not obvious from reading the guard in
isolation that the difference matters - it only showed up once a live click-through actually watched
where the URL bar ended up, not just whether the detail page failed to appear.

## What would break this

See [verification-log.md](verification-log.md)'s "What would break this" section - covers the real
API's `{id:int}` route constraint changing, an `id` field rename on `Quote`, and `authGuard`'s
client-only auth having no server-side backing.
