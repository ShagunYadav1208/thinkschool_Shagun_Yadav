# Verification log

Grounded in the real, running Week-1 `QuotesApi` (`day-1/piece3/QuotesApi`,
`ASPNETCORE_URLS=http://localhost:5310 dotnet bin/Release/net10.0/QuotesApi.dll`) - not a mock server,
not narrated. Two layers: the characterization unit-test suite (`quotes.service.spec.ts`, run via
`npm test`, Angular 22's vitest-based `ng test`), and a headless-Chromium Playwright pass driving the
actual `ng serve` dev build against the same live API.

## 1. Characterization tests - `npm test`, 10/10 passing

```
 Test Files  2 passed (2)
      Tests  10 passed (10)
   Duration  4.80s
```

| # | Test | Pins |
|---|------|------|
| 1 | `GET /api/quotes/?page=1&size=2` returns `Quote[]` shaped exactly `{id,author,text}` | Real success shape, no extra fields (e.g. no `createdAt`) |
| 2 | Adds a `Bearer` Authorization header | `authInterceptor` rewrites every request |
| 3 | Maps a real `page=0` 400 to a friendly `AppHttpError` | Verbatim `HttpValidationProblemDetails` body from curl, `friendlyMessage: "Page must be greater than 0."` |
| 4 | Maps a real `size=500` 400 to a friendly `AppHttpError` | Same, `friendlyMessage: "Size must be between 1 and 100."` |
| 5 | Does NOT retry a 400 - exactly one request | `retryInterceptor` never retries 4xx |
| 6 | Retries a 500 GET, succeeds on the 2nd attempt | Backoff + recovery path |
| 7 | Gives up after exhausting retries on a persistent 500 | 1 initial + 3 retries (300/600/1200ms), final `AppHttpError{status:500}` surfaces - not left hanging |
| 8 | Maps a real `GET /api/quotes/{id}` 404 (empty body) to a friendly `AppHttpError` | Empty-body error path, distinct from the ProblemDetails path |
| 9 | Does NOT retry a POST even on a 500 | Non-idempotent writes are never retried |
| 10 | (`app.spec.ts`, pre-existing) app bootstraps | Unrelated smoke test, left as-is |

All four ProblemDetails/empty-body bodies used in these tests (`PAGE_VALIDATION_PROBLEM`,
`SIZE_VALIDATION_PROBLEM`, the empty 404, and the 200 page shape) are pasted verbatim from real `curl`
output against the running API, not guessed from reading the controller source:

```
curl "http://localhost:5310/api/quotes?page=1&size=2"
-> 200 [{"id":17,"author":"Ada Lovelace","text":"..."},{"id":18,"author":"Grace Hopper","text":"..."}]

curl "http://localhost:5310/api/quotes?page=0&size=5"
-> 400 {"type":"...","title":"One or more validation errors occurred.","status":400,
        "errors":{"page":["Page must be greater than 0."]},"traceId":"..."}

curl "http://localhost:5310/api/quotes?page=1&size=500"
-> 400 {..., "errors":{"size":["Size must be between 1 and 100."]}, ...}

curl "http://localhost:5310/api/quotes/999999"
-> 404 (Content-Length: 0, no body)
```

## 2. Live-browser verification - Playwright + real `ng serve` + real API, 6/6 passing

`ng serve --port 4210` (proxying `/api` to the real API on 5310), driven with a headless-Chromium
Playwright script (no `chromium-cli` available on this machine, matching the approach used in
day-13/14). Every check below hit the actual network, not a mock.

| State/edge exercised | How | Result | Screenshot |
|---|---|---|---|
| **Loading** | Observed transiently between click and response (see `loading()` signal in the component; not separately screenshotted since it's sub-100ms against localhost, but present in the code path all six other checks depend on) | - | - |
| **Populated** | `page=1, size=10` against the real 5-quote dataset | 5 cards rendered | `1-populated.png` |
| **Empty** | `page=999, size=10` - past the end of the real data | "No quotes on this page." | `2-empty.png` |
| **4xx -> friendly message** | `page=0` | Title: "Page must be greater than 0.", HTTP 400 badge, field-error list | `3-friendly-4xx-page.png` |
| **4xx -> friendly message** | `size=500` | Title: "Size must be between 1 and 100." | `4-friendly-4xx-size.png` |
| **Auth header, live** | Captured the actual outgoing request's headers via Playwright's `request` event | `Authorization: Bearer demo-token` present | (captured programmatically, not a screenshot) |
| **Retry-with-backoff, live** | `page.route()` forced the *first* attempt at `page=1&size=3` to 500, let the 2nd through for real | 2 attempts observed, 3 cards rendered (the real data for that page) | `5-retry-recovered.png` |

```
PASS - populated state renders quote cards - 5 cards
PASS - empty state shows "No quotes on this page."
PASS - friendly 4xx message for page=0 - title="Page must be greater than 0." hint="HTTP 400"
PASS - friendly 4xx message for size=500 - title="Size must be between 1 and 100."
PASS - Authorization header present on live request - Bearer demo-token
PASS - retry-with-backoff recovers from a forced 500 in the live browser - attempts=2 cards=3

6/6 checks passed.
```

## 3. The concrete wrong assumption caught (and fixed) this session

**Assumption going in:** the new interceptors would only affect the new "Interceptors" tab's calls, so
a Playwright test could mock just the target request and reason about it in isolation.

**What actually happened:** the first live-browser retry test came back wrong -
`attempts=1 cards=5` instead of the expected `attempts>=2 cards=3` - even though the app's own behavior
was correct. Debugging with a request/response logger (not guessing) showed why:
`provideHttpClient(withInterceptors([...]))` in `app.config.ts` is **global** - it wires the retry and
auth interceptors onto *every* `HttpClient` call in the app, including the pre-existing
`QuotesStore.start()` background health-check poll (`interval(8000)` hitting plain `GET /api/quotes/`
with no `page`/`size`, unrelated to the tab under test). The Playwright script's
`page.route('**/api/quotes**', ...)` pattern was broad enough to intercept *that* poll too, so its
shared `attemptCount` counter got incremented by an unrelated request racing in the background,
corrupting the specific retry sequence the test was trying to observe.

**Fix:** narrowed the route matcher to the exact `page=`/`size=` combination under test, letting every
other request (the health-check poll, and any other in-flight call) pass through `route.continue()`
untouched. Re-ran - `attempts=2 cards=3`, correct.

**Why this matters beyond the test script:** it's a real fact about the shipped behavior, not just a
test artifact - a genuine network blip now takes up to ~2.1s longer (300+600+1200ms of backoff) to flip
the header bar's "API connected" dot to "disconnected," because the *existing* health-check poll now
retries too, not just the new tab's calls. That's arguably the right tradeoff (a poll shouldn't flip
red on one dropped packet) but it's a real, verified side effect of wiring the interceptors globally
rather than scoping them to the new feature, and it wasn't something either the brief or the first
implementation pass called out explicitly.

## 4. What would break this

- **The `errors` field name or nesting changes** (e.g. the real API moves from
  `Results.ValidationProblem` to a hand-rolled error shape without an `errors` dict): `toAppHttpError`'s
  `isProblemDetailsBody` check (`'title' in body || 'errors' in body`) would still technically match on
  `title` alone, but `fieldErrors` would silently become `null` and the friendly message would fall back
  to the generic `title` text instead of the specific field message - no compile error, no thrown
  exception, just a less useful message shown to the user.
- **A field gets renamed on `Quote`** (e.g. `text` -> `content`): `HttpClient.get<Quote[]>()` doesn't
  validate the response against the generic type at runtime - the app would silently render
  `undefined` where the quote text used to be, exactly the same failure mode documented in day-13's
  log for the un-intercepted service.
- **The API starts requiring real auth** (a genuine 401 instead of the placeholder Bearer token always
  succeeding): `authInterceptor` would keep attaching `Bearer demo-token` forever, and every request
  would fail identically regardless of `retryInterceptor` (401 isn't in the retryable set) - the
  friendly message would fall through to the generic "Something went wrong" branch in `toAppHttpError`,
  since there's no ProblemDetails body and no specific 401 branch written for it.
- **The retry interceptor's global scope** (see §3): any future feature added to this app that makes
  its own `HttpClient` calls inherits the same retry-with-backoff and error-mapping behavior
  automatically, whether it wants it or not - a one-off call meant to fail fast (e.g. a live-typing
  autocomplete ping) would silently pick up ~2.1s of retry delay on a flaky connection unless it's
  deliberately excluded (e.g. via an `HttpContext` token checked inside `retryInterceptor`, which
  nothing here currently does).
