# Brief to the agent (Claude Code)

**Target API (real, Week-1):** `QuotesApi` at
[thinkschool_Shagun_Yadav/day-1/piece3/QuotesApi](../../day-1/piece3/QuotesApi), run locally on
`http://localhost:5310` (`ASPNETCORE_URLS=http://localhost:5310 dotnet bin/Release/net10.0/QuotesApi.dll`).

- `GET /api/quotes/?page={page}&size={size}` - `page` defaults to 1, `size` defaults to 10 (max 100).
  Returns a **plain JSON array**, `[{ "id": 1, "author": "...", "text": "..." }, ...]` - no envelope, no
  `createdAt`. Confirmed live:
  ```
  curl "http://localhost:5310/api/quotes?page=1&size=2"
  -> 200 [{"id":17,"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal."}, ...]
  ```
- Invalid `page` (< 1) or `size` (outside [1, 100]) returns **400** with a real ASP.NET
  `HttpValidationProblemDetails` body (`content-type: application/problem+json`), confirmed live:
  ```
  curl "http://localhost:5310/api/quotes?page=0&size=5"
  -> 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1",
          "title":"One or more validation errors occurred.","status":400,
          "errors":{"page":["Page must be greater than 0."]},"traceId":"..."}

  curl "http://localhost:5310/api/quotes?page=1&size=500"
  -> 400 {..., "errors":{"size":["Size must be between 1 and 100."]}, ...}
  ```
  A page past the end of the data returns `200 []`, not an error - confirmed live
  (`page=999&size=10` -> `[]`). The dataset currently has 5 quotes total.
- `GET /api/quotes/{id}` - `200` with a quote, or `404` with an **empty body** (no ProblemDetails).
- `POST /api/quotes/` - `201` with the created quote, or `400` with the same
  `HttpValidationProblemDetails` shape (`errors: { author?: string[], text?: string[] }`).

**Goal:** wire `HttpClient` + functional interceptors on top of the existing Day 14 Angular app
(`day-14/piece2/quotes-list-detail`, copied into this folder unmodified as the starting point), in this
order:

1. **A characterization test first, green before any UI change.** `quotes.service.spec.ts`, using
   `HttpTestingController`, pinning the exact request/response shapes above - the success shape
   (`{id,author,text}[]`, no extra fields), the page=0 and size=500 400 bodies verbatim from the curl
   output (not invented), and the interceptor behavior itself (auth header present, a 400 is never
   retried, a 500 on a GET is retried, a 500 on a POST is not).
2. **`authInterceptor`** (functional, `HttpInterceptorFn`) - adds a `Bearer` auth header to every
   outgoing request. The real API has no auth of its own, so this proves the interceptor rewrites every
   request rather than proving a real auth flow.
3. **`retryInterceptor`** - retries **idempotent GETs only** with exponential backoff, and only on
   network failure (status 0) or 5xx. Never retries a 4xx (retrying `page=0` just gets `page=0`'s 400
   again) and never retries a non-GET (retrying a POST risks double-submitting it).
4. **`errorMappingInterceptor`** - maps a terminal `HttpErrorResponse` to a typed `AppHttpError`
   (`{ status, friendlyMessage, fieldErrors, raw }`). On a ValidationProblemDetails 400, the friendly
   message is the first field error message (e.g. "Size must be between 1 and 100."), not the generic
   "One or more validation errors occurred." title - that's the part a user actually needs to read.
   Registered so it only sees the response *after* `retryInterceptor` has exhausted its attempts.
5. A new tab in the existing app ("Interceptors") that calls the new paginated
   `QuotesService.getQuotesPage(page, size)` with free-typed page/size inputs, so a real 400 can be
   triggered live (not mocked) by typing `page=0` or `size=500`. Exercise four states explicitly:
   loading, populated, empty (a page past the end of the real data), and a 4xx surfaced as the
   `AppHttpError`'s friendly message - `@if`/`@else if`, no silent blank state.

Do not modify `day-1/piece3/QuotesApi` (or any other day's folder) - read-only reference for the
contract. Do not modify `day-14/piece2` - it's copied into this folder specifically so it can be
changed without touching prior days' work.
