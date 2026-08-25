# Verification log — Day 14 / Piece 1 (reactive create-quote form)

Target under test: `app-create-quote-form`, wired into the already-running
`day-13/piece2/quotes-list-detail` app (port 4201) against the real
`QuotesApi` backend. All checks below were run with Playwright (headless
Chromium) driving the live dev server - no mocked component, no fabricated
output. Scripts are not committed (scratch tooling); this file records what
each one did and what it returned.

## Method

- Real backend: `QuotesApi` running with its real SQLite `quotes.db`
  (3 seed quotes at the time of testing).
- Real frontend: `ng serve` on `http://localhost:4201`, proxying
  `/api/quotes/*` to the API per `proxy.conf.json`.
- Keyboard: `page.keyboard.press('Tab')` from a fresh page load, reading
  `document.activeElement` at each step.
- Screen-reader-relevant wiring: automated via **axe-core** (`axe.run()`
  scoped to `.create-quote-form`), not just eyeballed markup - catches
  things a manual read of the HTML misses (see the color-contrast finding
  below).
- Each state (empty / invalid / submitting / server-error / success) was
  driven in its own fresh page/tab. Submitting and server-error states used
  `page.route()` to control the real HTTP response timing/outcome without
  touching app code.

## Results

| # | Check | Result |
|---|---|---|
| 1 | Tab order from fresh load | `INPUT (search) → SELECT (author filter) → author-input → text-input → BUTTON (submit) → BUTTON (quote-list "View" buttons)` - correct, no skipped/trapped controls |
| 2 | axe-core violations, `.create-quote-form` scope | `[]` (zero) after the contrast fix (see Bugs below) |
| 3 | Focus after failed submit, both fields empty | `document.activeElement.id === "author-input"` ✓ |
| 4 | Focus after failed submit, only text invalid (author filled) | `document.activeElement.id === "text-input"` ✓ |
| 5 | Error text, empty fields | "Author is required." / "Text is required." ✓ (only after touched, not on load) |
| 6 | Error text, 101-char author | "Author must be 100 characters or fewer." - exact real limit from `CreateQuoteRequest.cs`, not a guessed round number |
| 7 | `aria-invalid`/`aria-describedby` wiring | Present only while invalid; `aria-describedby` id matches the rendered `<p id="author-error">`/`<p id="text-error">` exactly - no dangling id |
| 8 | Submitting state (1.5 s delayed POST via `page.route`) | Submit button disabled, text reads "Adding..." |
| 9 | Server-error state (`route.abort('failed')`) | Form shows "Couldn't add the quote. Please try again." via `role="alert"`; client-side field state untouched |
| 10 | Success (real POST, no route interception) | Quote list count went 3 → 4 immediately, no page reload; form reset to empty (`author-input` value `""`) |
| 11 | Character counter present and live | `0 / 100` / `0 / 1000` on load, updates on every keystroke |
| 12 | `aria-describedby` includes the counter id when the field is valid/untouched | `"author-counter"` (previously `null`) |
| 13 | Native `maxlength` actually caps input at the real API limit | Forcing a 150-char value into `author-input` (maxlength 100) results in a 100-char value |
| 14 | Counter styling at the limit | `counter-limit` class present at exactly 100/100 |
| 15 | Regression: focus-on-error and axe still clean after the counter change | Focus → `author-input`; axe violations → `[]` |

## UI restructure: tab navigation

The app was later copied to `day-14/piece1/quotes-list-detail` (day-13
untouched) and restructured into three tabs (Explore / Create / All
Quotes) sharing one `QuotesStore` singleton instead of one component owning
all the state. Re-verified after the restructure, against the app on
`http://localhost:4202`:

| # | Check | Result |
|---|---|---|
| 16 | Default active tab on load | "Explore" |
| 17 | Explore tab list renders | quote-list-items present, matches API count |
| 18 | All Quotes tab renders | one card per quote, full text, no truncation |
| 19 | Create tab form still functions | axe violations `[]`; focus-after-failed-submit still lands on `author-input` |
| 20 | Real create → auto-switches to Explore, new quote visible immediately | active tab becomes "Explore", list count +1, new item's author matches what was submitted |
| 21 | Keyboard tab order across nav + content, fresh load | `Explore → Create → All Quotes → search input → author select → first quote-list-item` - nav tabs are reachable and in a sensible position before the page content |
| 22 | axe-core, full `main` region, all three tabs | `[]` on all three (see bug below - this scope had never been checked before) |

## Bugs caught and fixed

**1. No visible character limit until the user already broke it (the concrete catch from live manual review).**
After the agent's form was working end-to-end, manually driving the running
app at `http://localhost:4201` surfaced a real UX/a11y gap the earlier
Playwright pass didn't: the 100/1000 character limits existed only as
invisible `Validators.maxLength` checks. A sighted user had no `maxlength`
attribute stopping them, no counter, and no hint text - the limit only
became visible *after* they'd already typed past it, blurred the field, and
triggered the error. A screen-reader user had even less: nothing announced
the limit at all until failure.

Fix: added a live "N / limit" counter per field (`author-counter` /
`text-counter`, `aria-live="polite"`), wired to the control's own
`valueChanges` via `toSignal()` so pasted text and `form.reset()` both stay
in sync automatically; a native `maxlength` attribute (`limits.author` /
`limits.text` - the same constants the validators use, not a re-typed
number) so the browser genuinely stops accepting input past the real API
limit instead of silently trimming it server-side; and `aria-describedby`
now always includes the counter id (plus the error id when invalid), so
assistive tech reads the running count on every keystroke, not just the
error after the fact.

Re-verified live with Playwright:
- Counter starts at `0 / 100` / `0 / 1000` and updates live while typing.
- `aria-describedby` on a valid, untouched field is `"author-counter"` -
  present even with no error.
- Simulating a 150-character entry into the author field actually stops at
  100 characters (native `maxlength` enforced, not just the validator).
- At exactly 100/100 the counter picks up the `counter-limit` (red/bold)
  styling.
- Regression check: focus-after-failed-submit still lands on
  `author-input`, and axe still reports zero violations on the changed
  markup.

**2. Missing focus management on failed submit (the original intended catch).**
The agent's first draft called `this.form.markAllAsTouched()` on an invalid
submit, which correctly makes the errors *visible* (`aria-invalid`,
`aria-describedby`, error text all appear) but never moves keyboard or
screen-reader focus anywhere - confirmed with Playwright:
`document.activeElement` stayed on the submit button after a failed submit.
A screen-reader user would see nothing announced and have to hunt for the
error manually.

Fix went through two iterations:
- First attempt queried the DOM for `[aria-invalid="true"]` inside a
  `queueMicrotask`. This still failed the re-check (focus stayed on the
  submit button) - because this is a **zoneless** Angular app, a plain
  `FormGroup` state change doesn't get flushed to the DOM on the same
  microtask tick, so the query ran before `aria-invalid` was actually
  written to the DOM.
- Final fix: `@ViewChild` references to the two inputs, and checking
  `this.form.controls.author.invalid` / `.text.invalid` directly (form
  validity is available synchronously, no DOM/render-timing dependency at
  all). Re-verified: focus lands on `author-input` when both are invalid,
  and on `text-input` when only text is invalid.

**3. Color-contrast violation on the submit button (bonus, axe-caught).**
Not something a manual read of the template would catch. axe-core flagged
`button[type="submit"]` as a **serious** `color-contrast` violation - white
text on `#6366f1` measures ~4.5:1, right at the WCAG AA edge and evidently
failing axe's precise check. Fixed by darkening to `#4338ca` (~8:1
contrast). Re-ran axe: zero violations.

**4. Two more real color-contrast violations, found only once axe could
scan the whole page (bonus).**
The original verification only ever ran axe scoped to
`.create-quote-form`, because that was the only new markup at the time.
Once Explore and All Quotes became real, separately-reachable tabs, running
axe against the full `main` region on each one surfaced two violations that
had been sitting in the *original* Day 13 markup the whole time, unnoticed:
`.subtitle`/`.status` text at `#8a8a8f` (~3.4:1, below the 4.5:1 AA
threshold for normal text) and the author-name text at the same borderline
`#6366f1` the submit button used to use. Fixed by darkening to `#6b6b70`
(~5.3:1) and reusing the already-verified `#4338ca` (~8:1). Re-ran axe on
all three tabs afterward: `[]` everywhere.

No other bugs found across the states/edges above. Test data (quotes
created during testing) was deleted from the real API afterward, restoring
`quotes.db` to its original 3-quote seed state plus one quote the user
added themselves while manually checking the app (`Sumit Sharma`) -
left in place since it's real user data, not test data.

## What would break this

- **Field rename** (e.g. `author` → `authorName` on the API side): the
  form's `formControlName`s and the 400-response field-key lookup
  (`err.error.errors[field]`) both key off the exact lowercase names
  `author`/`text` - a rename silently stops mapping server-side validation
  errors onto the right control (they'd just vanish, since
  `this.form.get(field)` would return `null`).
- **New required field added to `CreateQuoteRequest`**: the form has no
  generic/dynamic field rendering - a new required API field would make
  every submission 400 with no visible control to fix it, since the extra
  error key wouldn't have a matching `FormControl` to attach to.
- **Tightened length limits** (e.g. author max lowered to 50): client-side
  validators still enforce 100, so a value the form accepts would still be
  rejected by the server; the `server`-tagged error path only handles it
  gracefully because the fallback error surfaces the server's real message,
  but the wrong client-side maxlength text would show first if it hadn't
  actually reached the server.
- **`/api/quotes/` moved off the proxied path** or the API stops returning
  the `{ errors: { field: [...] } }` shape on 400 - the `err.status === 400
  && err.error?.errors` check would fail, and every validation failure
  would fall through to the generic "Couldn't add the quote" message
  instead of pointing at the actual invalid field.
