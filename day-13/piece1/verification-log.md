# Verification log

Everything below is from an actual run: the real `QuotesApi` (Week-1, `day-1/piece3/QuotesApi`) built
in Release and run on `http://localhost:5310`, the Angular app served via `ng serve` on
`http://localhost:4200`, driven by a headless Chromium via Playwright (no `chromium-cli` on this
machine, so a small Node driver script filled in). Screenshots for every state are in
[verification-screenshots/](verification-screenshots).

## States and edges actually exercised

| # | State | How it was produced | Result |
|---|---|---|---|
| 1 | Empty - API has zero quotes | `QuotesApi`'s `quotes.db` was genuinely empty on first run (confirmed via `curl http://localhost:5310/api/quotes/` -> `[]`) | `"No quotes yet."` - [screenshot](verification-screenshots/1-empty-state.png) |
| 2 | Populated | `POST /api/quotes/` (the real API's own endpoint) x3, then reload | All 3 render, correct author/text - [screenshot](verification-screenshots/2-populated-state.png) |
| 3 | Computed reacting live to both signals | Typed `"Grace"` into the search box - **no page reload** | List narrows from 3 to 2 items, both Grace Hopper, in the same page load - [screenshot](verification-screenshots/3-filtered-live-state.png) |
| 4 | Search matches zero, but data exists | Typed a nonsense string into the search box | `"No quotes match \"zzz-no-such-author\"."` (correctly different message from state 1) - [screenshot](verification-screenshots/4-zero-matches-state.png) |

State 3 is the one that actually proves the point of this exercise: `filteredQuotes` is a
`computed()` over `quotes` and `searchTerm`, and typing produced a re-rendered list with zero calls to
`ChangeDetectorRef`, zero Zone.js, and no reload - `provideZonelessChangeDetection()` plus a signal
write in `onSearchInput()` was the entire mechanism. Verified by watching `<li>` count change from 3
to 2 in the same page instance, then a screenshot.

## One concrete bug caught and fixed: wrong field assumed on the real API

**Assumption made:** the first draft of `quote.model.ts` included a `createdAt: string` field, and the
template rendered `quote.createdAt` in the byline. This is a *plausible* mistake, not a random one -
several other Quotes APIs in this same repo (`day-3/piece6`, `day-5/piece4`) do have a timestamp
field, so pattern-matching against sibling projects produces exactly this wrong guess.

**How it was caught:** not by inspection - by checking the real source
(`day-1/piece3/QuotesApi/Models/Quote.cs`, read directly, not summarized) and then, to remove any
doubt, actually running that API and calling it:

```
$ curl -X POST http://localhost:5310/api/quotes/ -d '{"author":"Ada Lovelace","text":"..."}'
{"id":1,"author":"Ada Lovelace","text":"..."}
$ curl http://localhost:5310/api/quotes/
[{"id":1,"author":"Ada Lovelace","text":"..."}]
```

No `createdAt` anywhere in the real response. The field was removed from `Quote`, and the template's
`- {{ quote.author }} - posted {{ quote.createdAt }}` was cut down to `- {{ quote.author }}`. The
test quote used to confirm this was deleted afterward (`DELETE /api/quotes/1`) so the real API's
database was left exactly as found - empty.

## A second bug the browser test caught that a unit test wouldn't have: CORS

Once the app was actually loaded in a real browser and pointed at the real API on a different port,
every request failed:

```
Access to fetch at 'http://localhost:5310/api/quotes/' from origin 'http://localhost:4200' has been
blocked by CORS policy: No 'Access-Control-Allow-Origin' header is present on the requested resource.
```

`QuotesApi` has no CORS policy configured, and the brief says not to modify that project. Fixed on the
Angular side instead - `proxy.conf.json` + `angular.json`'s `serve.options.proxyConfig`, with
`environment.apiBaseUrl` changed from an absolute `http://localhost:5310/...` to a relative
`/api/quotes/` so the dev server proxies it. This is also the more idiomatic Angular fix regardless of
whose fault the CORS gap is - hardcoding a full origin into a service was itself something a real
review should have flagged even before the CORS error surfaced.

## A third, smaller thing caught along the way

The empty-list message originally read `No quotes match ""` when the API had *zero* quotes and the
search box was empty - technically not wrong, but confusing: it looks like a search problem when
there's no data at all. Split into two distinct messages (`quotes().length === 0` -> "No quotes yet.",
`filteredQuotes().length === 0` -> "No quotes match \"...\"") - see state 1 vs. state 4 above.

## What would break if the API contract changed

- **Renaming a field** (`author` -> `authorName`, say): the `Quote` interface has no runtime
  validation - a rename on the server would silently produce `undefined` in the template (Angular
  templates don't throw on `undefined` interpolation, they just render nothing), not a compile error,
  since `HttpClient.get<Quote[]>()` trusts the generic type parameter without checking the actual JSON
  against it. The `createdAt` bug above is exactly this failure mode, just self-inflicted instead of
  caused by a real server-side change.
- **Wrapping the array in an envelope** (`{ items: [...], totalCount: N }` instead of a bare array):
  `getQuotes()` would still compile and run, but `quotes.set(quotes)` would set an object where an
  array was expected, and `filteredQuotes`'s `.filter()` call would throw at runtime the first time
  anyone typed into the search box - a very different failure from the field-rename case, and one
  that *would* surface immediately and loudly instead of silently.
- **Adding CORS to the real API later**: the dev proxy would keep working unchanged (it doesn't care
  whether the target needs CORS or not), but a production build pointed at an absolute URL instead of
  through a proxy would then start working too - meaning this specific fix (the proxy) would become
  optional rather than required, not something that needs undoing.
