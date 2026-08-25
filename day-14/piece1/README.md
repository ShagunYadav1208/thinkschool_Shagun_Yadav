# Day 14 / Piece 1 — Reactive form + accessibility

A reactive create-a-quote form built against the real Week-1 `POST /api/quotes/`
contract. Originally wired straight into the Day 13 app; per a later request
the whole Day 13 app was copied into
[quotes-list-detail/](quotes-list-detail/) inside this folder (day-13 itself
is untouched) so the UI changes below could be made without touching prior
days' work. Everything in this README from this point describes the app as
it now stands in `day-14/piece1/quotes-list-detail`.

## UI update: tab navigation (Explore / Create / All Quotes)

The single stacked page (search bar → create form → list+detail, all on one
scroll) was restructured into three tabs in a persistent nav bar, so each
concern gets its own screen instead of competing for space:

- **Explore** - the original search box + author dropdown + list/detail
  click-through view.
- **Create** - the reactive create-quote form, on its own.
- **All Quotes** - a new, simpler unfiltered card grid of every quote (full
  text, no truncation, no click-through) for quickly skimming everything.

State (the quotes list, search/filter, selected detail, API-connected
status) moved out of the old `QuoteListDetail` component and into a shared
[quotes-store.ts](quotes-list-detail/src/app/quotes-store.ts) singleton
service, so a quote created on the Create tab is visible on Explore/All
Quotes immediately - the store isn't tied to any one tab's component
lifetime. Submitting the create form now also auto-switches to the Explore
tab as a visible success confirmation.

The API-connected indicator lives in the persistent nav bar (visible from
every tab); the old `QuoteListDetail` component was renamed to
[explore-view.ts](quotes-list-detail/src/app/explore-view/explore-view.ts)
to match its new, narrower scope.

## 1. The brief

See [brief-to-agent.md](brief-to-agent.md) - written from the real API
contract (`day-1/piece3/QuotesApi/Models/CreateQuoteRequest.cs` and live
curl checks against the running API for the exact 400 error shape and
character limits), not guessed.

## 2. The agent's output

The form itself, unchanged since the original build:

- [create-quote-form.ts](quotes-list-detail/src/app/create-quote-form/create-quote-form.ts)
- [create-quote-form.html](quotes-list-detail/src/app/create-quote-form/create-quote-form.html)
- [create-quote-form.css](quotes-list-detail/src/app/create-quote-form/create-quote-form.css)

Supporting wiring (`quotes.service.ts` gained `CreateQuoteRequest` +
`createQuote()` calling the real `POST /api/quotes/`), plus the tab
restructure described above:

- [quotes-store.ts](quotes-list-detail/src/app/quotes-store.ts) - the
  shared state singleton (list, search/filter, detail, API status),
  extracted from the old `QuoteListDetail` component.
- [explore-view/](quotes-list-detail/src/app/explore-view/) - search +
  author filter + list/detail (the old `QuoteListDetail`, renamed and
  reading from the store instead of owning its own state).
- [all-quotes-view/](quotes-list-detail/src/app/all-quotes-view/) - the new
  unfiltered card-grid tab.
- [app.ts](quotes-list-detail/src/app/app.ts) / [app.html](quotes-list-detail/src/app/app.html) / [app.css](quotes-list-detail/src/app/app.css) -
  the nav bar (tabs + API indicator) and per-tab routing via `@switch`.

Key design points, straight from the final code:

- Validators (`Validators.required`, `Validators.maxLength(100|1000)`)
  match the real API limits exactly.
- Errors only shown once a control is `touched || dirty` - not on initial
  render.
- `aria-invalid` / `aria-describedby` bound conditionally to the id of the
  actual rendered error `<p role="alert">`.
- On failed submit: `markAllAsTouched()` **and** focus moved to the first
  invalid control via `@ViewChild` + direct `FormGroup` validity checks
  (see the bug below for why it's not a DOM query).
- Server-side 400 errors are mapped back onto the matching control via
  `setErrors({ server: message })`, reusing the same error-display path as
  client-side validation errors.
- Any other failure (network error, unexpected status) shows a form-level
  `role="alert"` message instead of silently failing.
- Live "N / limit" character counter per field, driven off the control's
  own `valueChanges` (`toSignal()`), with `aria-live="polite"` and a native
  `maxlength` attribute that actually stops input at the real API limit
  (added after manually driving the app surfaced the gap - see below).

## 3. Verification log

Full detail in [verification-log.md](verification-log.md); screenshots in
[verification-screenshots/](verification-screenshots/). Summary:

- Tab order, `aria-invalid`/`aria-describedby` wiring, and axe-core
  (`.create-quote-form` scope) all verified against the live app - not
  eyeballed.
- All five required states exercised: empty, invalid (both a
  both-fields-empty case and an only-text-invalid case), submitting
  (delayed real request via `page.route`), server-error (aborted real
  request), success (real POST, list updates immediately, form resets).
- **Bug caught and fixed (the concrete catch, found by manually driving the
  live app, not by the automated suite):** the 100/1000 character limits
  existed only as invisible validators - no `maxlength` attribute, no
  counter, no hint. A user only discovered the limit after already typing
  past it and getting an error. Fixed with a live "N / limit" counter
  (`aria-live="polite"`, wired to the same limit constants the validators
  use) and a real `maxlength` attribute that stops input at the actual API
  limit. Re-verified with Playwright, including a regression check that
  focus-on-error and axe's zero violations still held.
- **Bug caught and fixed (from the earlier automated pass):** the agent's
  first draft made errors *visible* on failed submit but never moved focus
  there - a screen-reader user would get no signal that anything happened.
  Fixed with `@ViewChild` + `FormGroup` validity checks (a DOM-query-based
  first attempt failed because this is a zoneless app - see the log for
  why).
- **Bonus bug caught by axe, not by eye:** the submit button's color
  contrast (`#6366f1` on white, ~4.5:1) was flagged as a serious violation;
  fixed by darkening to `#4338ca` (~8:1).
- **Second bonus, found while verifying the tab restructure:** widening the
  axe scope from just `.create-quote-form` to the whole `main` region (now
  possible/necessary since Explore and All Quotes are real, separate tabs)
  surfaced two more real, pre-existing `color-contrast` violations that had
  never been audited before: `.subtitle`/`.status` text (`#8a8a8f`, ~3.4:1)
  and the author name text (`#6366f1`, same borderline ratio as the button
  bug). Fixed by darkening to `#6b6b70` (~5.3:1) and reusing the
  already-proven `#4338ca` (~8:1). Re-verified zero violations on all three
  tabs afterward.

## Running it

```bash
cd day-14/piece1/quotes-list-detail
npm install
npm start -- --port 4202
```
Proxies `/api/*` to `http://localhost:5310` (`proxy.conf.json`) - start
`day-1/piece3/QuotesApi` on that port first (`ASPNETCORE_URLS=http://localhost:5310 dotnet bin/Release/net10.0/QuotesApi.dll`).

## GitHub

Pushed to the `thinkbridge-thinkschool` org repo - link to follow once
pushed (see Notes for mentor).

## Notes for mentor

- The app now lives entirely inside `day-14/piece1/quotes-list-detail` - a
  copy of the Day 13 app, per a later request to make further UI changes
  without touching `day-13/piece2`. `day-13/piece2` itself is unmodified.
- `day-1/piece3/QuotesApi` was read-only reference for the contract; no
  source files there were modified.

## What I learned this session

Automated checks (Playwright + axe) only verify what you thought to ask
them. The character-limit gap slipped past a full automated pass with zero
axe violations, because axe has no opinion on "does the user know the limit
exists before they hit it" - that only showed up once I actually opened the
running app myself and typed into it like a real user would. `markAllAsTouched()`
also makes validation errors *visible*, but visibility and *focus* are two
separate things - an assistive-technology user needs the second one, and
it's easy to ship a form that "looks" accessible while still leaving a
screen-reader user stranded with no idea anything failed. Also: in a
zoneless Angular app, `queueMicrotask()` is not a safe way to "wait for the
DOM to catch up" after a signal/FormGroup change - checking the source of
truth (form validity) directly sidesteps the whole timing question.

## What would break this

See the "What would break this" section of
[verification-log.md](verification-log.md) - covers a field rename, a new
required API field, tightened server-side limits, and a change to the 400
error response shape.
