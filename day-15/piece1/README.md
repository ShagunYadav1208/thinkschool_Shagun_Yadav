# Day 15 / Piece 1 — HttpClient + interceptors

Built on top of [day-14/piece2](../../day-14/piece2)'s Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/) in this folder (day-14 itself is untouched) so this
exercise's interceptor work could land without touching prior days' work - same pattern day-14/piece1
used against day-13/piece2.

Like day-13/14, the deliverable here is the direct-and-verify loop itself: the brief given to the
agent, the agent's resulting code verbatim, and a verification log grounded in the real, running Week-1
API and a real headless browser - not narrated, not assumed.

## 1. The brief

Full text in [brief-to-agent.md](brief-to-agent.md). The real API it names:

> **Target API (real, Week-1):** `QuotesApi` at `day-1/piece3/QuotesApi`
> - `GET /api/quotes/?page={page}&size={size}` - plain array `[{id,author,text}]`, invalid `page`/`size`
>   returns a real `HttpValidationProblemDetails` 400 (`errors: {page?: [...], size?: [...]}`), both
>   confirmed live via curl.
>
> **Goal:** a characterization test pinning that contract, written and green *before* any UI; then
> `authInterceptor` (Bearer header), `retryInterceptor` (idempotent GETs only, exponential backoff,
> never on 4xx or non-GET), and `errorMappingInterceptor` (ProblemDetails -> typed `AppHttpError` with a
> friendly message); then a new "Interceptors" tab exercising loading/populated/empty/friendly-4xx
> states against the real API.

## 2. The agent's output

**`src/app/core/http-error.ts`** - the typed error every interceptor consumer works with, and the
mapper from a real `HttpErrorResponse`:

```typescript
export interface AppHttpError {
  status: number;
  friendlyMessage: string;
  fieldErrors: Record<string, string[]> | null;
  raw: unknown;
}

export function toAppHttpError(error: HttpErrorResponse): AppHttpError {
  const body = error.error;

  if (isProblemDetailsBody(body)) {
    const fieldErrors = body.errors ?? null;
    const friendlyMessage =
      (fieldErrors && firstFieldError(fieldErrors)) ?? body.detail ?? body.title ?? 'Request failed.';
    return { status: error.status, friendlyMessage, fieldErrors, raw: body };
  }
  if (error.status === 0) {
    return { status: 0, friendlyMessage: "Can't reach the server. Check your connection and try again.", fieldErrors: null, raw: error.error };
  }
  if (error.status === 404) {
    return { status: 404, friendlyMessage: 'Not found.', fieldErrors: null, raw: error.error };
  }
  return { status: error.status, friendlyMessage: 'Something went wrong. Please try again.', fieldErrors: null, raw: error.error };
}
```

**`src/app/core/auth.interceptor.ts`:**

```typescript
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem('auth_token') ?? 'demo-token';
  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
```

**`src/app/core/retry.interceptor.ts`:**

```typescript
const MAX_RETRIES = 3;
const BASE_DELAY_MS = 300;

function isRetryable(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 0 || error.status >= 500);
}

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') return next(req);

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      delay: (error, retryCount) => {
        if (!isRetryable(error)) throw error;
        return timer(BASE_DELAY_MS * 2 ** (retryCount - 1));
      },
    })
  );
};
```

**`src/app/core/error-mapping.interceptor.ts`:**

```typescript
export const errorMappingInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) return throwError(() => toAppHttpError(error));
      return throwError(() => error);
    })
  );
```

**`src/app/app.config.ts`** (registration order is the actual behavior - see comment):

```typescript
provideHttpClient(
  withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])
),
```

**`src/app/quotes.service.ts`** gained the paginated call:

```typescript
getQuotesPage(page: number, size: number): Observable<Quote[]> {
  const params = new HttpParams().set('page', page).set('size', size);
  return this.http.get<Quote[]>(environment.apiBaseUrl, { params });
}
```

**New tab:** [interceptors-view/](quotes-list-detail/src/app/interceptors-view/) - free-typed page/size
inputs (so a real 400 can be triggered live by typing `page=0` or `size=500`), four explicit template
states (loading / populated / empty / 4xx-as-friendly-message), wired into
[app.ts](quotes-list-detail/src/app/app.ts) / [app.html](quotes-list-detail/src/app/app.html) as a
fifth "Interceptors" tab alongside Explore / Create / Signal Forms / All Quotes.

**Characterization test:** [quotes.service.spec.ts](quotes-list-detail/src/app/quotes.service.spec.ts)
- written and passing *before* the Interceptors tab existed, per the brief. 10 tests: the real success
shape, the real page=0 and size=500 ValidationProblemDetails bodies pasted verbatim from curl, the real
404's empty body, auth-header presence, retry-then-succeed, retry-exhaustion, and "never retries a 4xx
or a POST."

## 3. Verification log

Full detail, states/edges table, and screenshots in **[verification-log.md](verification-log.md)** -
summary:

- **10/10 unit tests pass** (`npm test`) against `HttpTestingController`-mocked responses pinned to real
  curl output, not invented shapes.
- **6/6 live-browser checks pass** - a real `ng serve` build, headless Chromium via Playwright, against
  the real API on `localhost:5310`: populated (5 real quotes), empty (`page=999`), friendly 4xx for
  both `page=0` and `size=500`, a live-captured `Authorization: Bearer` header, and a forced-500 that
  actually retries and recovers in the browser (not just in a unit test).
- **The concrete wrong assumption caught:** the interceptors are wired globally in `app.config.ts`, so
  they also apply to the pre-existing `QuotesStore` background health-check poll, not just the new tab.
  This first showed up as a broken Playwright retry test (`attempts=1` instead of `2+`) because the
  test's route mock was catching that unrelated poll too; fixing the test surfaced the real, verified
  consequence - a genuine network blip now takes up to ~2.1s longer to flip the header's "API
  disconnected" indicator, since the poll retries before giving up. Full mechanism in the log.
- **What would break the contract:** an `errors`-shape change degrades silently to a generic message; a
  field rename on `Quote` is a silent `undefined`, not a compile error; real auth (a genuine 401) would
  make every request fail identically since `authInterceptor`'s placeholder token never adapts and 401
  isn't retryable.

## Running it

```bash
cd day-15/piece1/quotes-list-detail
npm install
npm test              # 10/10, no server needed
npm start -- --port 4210
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

- `day-1/piece3/QuotesApi` was read-only reference for the contract; no source files there were
  modified. `day-14/piece2` is likewise untouched - this folder is a copy.
- The dataset behind the real API currently has 5 quotes (`GET /api/quotes?page=1&size=100` → ids
  17, 18, 19, 22, 26) - the `verification-log.md` numbers (5 cards populated, 3 cards on
  `page=1&size=3`) are read against that live count, not a fixture.
- `node_modules/` and `.angular/` were copied wholesale from day-14/piece2 rather than reinstalled, to
  avoid a redundant network install of an already-resolved `package-lock.json`; both remain gitignored.

## What did I learn this session?

Registering an interceptor in `provideHttpClient(withInterceptors([...]))` is an app-wide decision, not
a per-feature one - it was easy to *design* `retryInterceptor` while only thinking about the new
paginated call, and easy to *verify* it that way too (a unit test with its own isolated
`HttpTestingController` genuinely doesn't see the rest of the app). It took a real browser, with the
real, still-running `QuotesStore` poll alongside the feature under test, for the global-scope
consequence to actually surface as a wrong result instead of staying a theoretical concern.

## What would break this

See [verification-log.md](verification-log.md)'s "What would break this" section - covers a
ProblemDetails shape change, a `Quote` field rename, a switch to real (non-placeholder) auth, and the
global retry scope applying to future features that might want to fail fast instead.
