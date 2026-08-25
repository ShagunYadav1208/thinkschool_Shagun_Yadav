# Verification log — Day 14 / Piece 2 (Signal Forms rebuild)

Target under test: `app-create-quote-form-signal`, the Create tab of the app in
`day-14/piece2/quotes-list-detail` (a copy of `day-14/piece1`, restructured to
use `@angular/forms/signals` instead of `ReactiveFormsModule` for the create
form; Explore/All Quotes tabs and the rest of the app are unchanged). Verified
against the real, running `QuotesApi` on `http://localhost:4203`, using
Playwright (headless Chromium) - no mocked component, no fabricated output.

**Package actually installed:** `@angular/forms@22.1.3`, confirmed by reading
`node_modules/@angular/forms/types/signals.d.ts` and `_structure-chunk.d.ts`
directly before writing any code - the brief and the implementation are
grounded in the real, installed preview API, not documentation memory.

## States/edges exercised

| # | State | Result |
|---|---|---|
| 1 | Pristine (fresh load) | `.error` count 0, counters read `0 / 100` and `0 / 1000` |
| 2 | Dirty (typing, not yet blurred) | No error shown while typing (matches reactive-forms parity: errors only after touch/dirty gate); typing 150 chars into author is capped at 100 by a **native** `maxlength` attribute the `[formField]` directive sets automatically - counter reads `100 / 100` |
| 3 | Invalid, both fields empty, submit | Error text + `aria-invalid` appear, focus moves to `author-input` |
| 4 | Invalid, only text empty, submit | Focus moves to `text-input` |
| 5 | Submitting (delayed real request via `page.route`) | Submit button disabled, reads "Adding..." - driven by the field's own **built-in** `submitting()` signal, no hand-rolled flag needed |
| 6 | Server/network error (aborted real request) | Form shows "Couldn't add the quote. Please try again." |
| 7 | Server-side 400 targeting a specific field (simulated real API response shape) | Error text ("Author already exists.") attached to the right field via `submit()`'s `fieldTree`-targeted error mechanism, **and** focus moves there (fixed - see below) |
| 8 | Success (real POST) | New quote appears in Explore's list immediately, active tab auto-switches to Explore, form resets |
| 9 | axe-core, `.create-quote-form` scope | `[]` (zero violations) |
| 10 | Keyboard tab order within the form | `author-input → text-input → submit button` - correct |

## A11y verification method

Same as `day-14/piece1`: Playwright driving the real running app (keyboard
`Tab` presses + `document.activeElement` checks for focus-on-error) and
automated axe-core scans, not eyeballing the markup.

## The concrete bug caught and fixed

**The agent's first draft assumed `submit()` gives touch-marking and focus
management "for free," the same way a hand-wired reactive-forms
`markAllAsTouched()` + `@ViewChild.focus()` call does. It doesn't - and
worse, the missing `novalidate` on the `<form>` meant the browser's own
native HTML5 validation silently intercepted the whole submission before
Angular even saw it.**

Confirmed live with Playwright, submitting the empty form on the first
draft:
```
1. Pristine: author aria-invalid = null (expect null)
2. After clicking submit on EMPTY form:
   #author-error present in DOM: 0   <- BUG: no error shown at all
   author aria-invalid: null
   document.activeElement: author-input
3. After manually clicking into then out of author field:
   #author-error present: 1   <- validator DOES fire once touched manually
```
Digging into *why* nothing appeared (not just that it didn't) surfaced two
compounding causes, both confirmed directly against the live DOM rather than
guessed:
```
form has novalidate: false
author-input.required (native DOM property): true       <- FormField auto-sets this
author-input.maxLength (native DOM property): 100        <- and this
native "submit" event fired on empty-form click: false    <- browser blocked it
```
`FormField` auto-applies real native `required`/`maxlength` DOM attributes
for the validators declared in the schema (a genuine Signal Forms behavior,
confirmed in the type declarations' `FormUiControl` interface) - and because
the template never had a `novalidate` attribute on the `<form>` (the reactive
version's template did), the browser's own constraint validation intercepted
the click and blocked the `submit` event from ever firing. So even the
missing-focus assumption never got a chance to matter: Angular's `onSubmit()`
never ran at all for an invalid submission attempt.

**Made it fix, in two parts:**
1. Added `novalidate` to the `<form>` element, so the browser defers entirely
   to the custom, accessible error UI (matching the reactive-forms version).
2. Added an `onInvalid` handler to `submit()`'s options, calling
   `quoteForm().markAsTouched()` (Signal Forms' equivalent of
   `markAllAsTouched()`) and `quoteForm.author().focusBoundControl()` /
   `quoteForm.text().focusBoundControl()` (the equivalent of the reactive
   version's `@ViewChild` + `.focus()`) - `onInvalid` exists specifically for
   this in the real `FormSubmitOptions` type; it is not automatic.

Re-verified: same empty-form submit now shows both errors, sets
`aria-invalid`, and moves focus to `author-input`.

**A second, related gap found during the same verification pass (fixed in
the same pass, not treated as a second full bug cycle):** a *server-side*
400 targeting a field (via `submit()`'s `action` returning a
`fieldTree`-targeted error) attaches the error correctly but - unlike the
client-validation path - does not go through `onInvalid` and so does not
move focus either. Fixed by checking, right after `submit()` settles,
whether a field is still invalid because of a server-rejected submission
(guarded by a local flag so a successful submission - which resets the model
and makes the now-empty fields "invalid" again per `required()` - doesn't
steal focus back). An earlier attempt at this fix used `queueMicrotask()`
inside the `action`'s `catch` block and raced the framework's own
error-application - the same zoneless-timing trap hit in `day-14/piece1`'s
reactive-forms version; moving the check to after `await submit(...)`
resolves sidesteps it entirely.

## What breaks if the Week-1 API contract changes

Same real exposure points as the reactive-forms version, plus one specific
to this rebuild:
- **Field rename** (`author` → `authorName`): the schema's `required()`/
  `maxLength()` calls are bound to `p.author` by property access, so a
  rename is a compile-time TypeScript error here (a genuine Signal-Forms-
  specific improvement over reactive forms' string-keyed
  `formControlName="author"`, which fails silently/at runtime instead) - but
  the server-error mapping (`(this.quoteForm as any)[fieldName]`) is still
  string-keyed and duck-typed, so a mismatched `fieldName` from the API
  silently produces `undefined` and the server error is dropped with no
  warning.
- **New required API field**: same gap as the reactive version - no control
  exists to attach the new field's error to.
- **Tightened server-side limit**: the client-side `maxLength()` validator
  (and its auto-applied native `maxlength` attribute) would still allow the
  old, looser limit through to the server, which would then reject it -
  handled by the server-error path, but the user would see the generic
  server message rather than a specific client-side one until they hit
  submit.
- **400 response shape changes**: identical exposure to the reactive-forms
  version - the `err.status === 400 && err.error?.errors` check is
  structural, not derived from the schema.
