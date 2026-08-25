# Day 14 / Piece 2 — Signal Forms preview rebuild

The same create-a-quote form from `day-14/piece1`, rebuilt with Angular's
**Signal Forms preview API** (`@angular/forms/signals`) instead of
`ReactiveFormsModule`, against the same real Week-1 `POST /api/quotes/`
contract. This lives in `day-14/piece2/quotes-list-detail` - a copy of the
piece1 app (`day-14/piece1` itself is untouched, and its reactive-forms
`create-quote-form` component was kept in piece2 too, not replaced).

The app now has **four tabs**: Explore, **Create** (the original
reactive-forms form, unchanged from piece1), **Signal Forms** (the new
rebuild), and All Quotes - so both versions are live side by side in the
same running app for a direct comparison, not just a written one.

## 1. The brief

See [brief-to-agent.md](brief-to-agent.md). Same real endpoint and field
constraints as piece1 (`day-1/piece3/QuotesApi/Models/CreateQuoteRequest.cs`
- `author`/`text`, 100/1000 char limits, both required). Before writing the
brief, the actual installed `@angular/forms/signals` API was read directly
from `node_modules/@angular/forms/types/signals.d.ts` and
`_structure-chunk.d.ts` (real package: `@angular/forms@22.1.3`) rather than
relying on memory of a preview API that changes fast.

## 2. The agent's output

[create-quote-form-signal.ts](quotes-list-detail/src/app/create-quote-form-signal/create-quote-form-signal.ts) /
[.html](quotes-list-detail/src/app/create-quote-form-signal/create-quote-form-signal.html) /
[.css](quotes-list-detail/src/app/create-quote-form-signal/create-quote-form-signal.css)
(the `.css` is reused verbatim from piece1's already-contrast-checked form).

Key structure:
```typescript
protected readonly model = signal({ author: '', text: '' });

protected readonly quoteForm = form(this.model, (p) => {
  required(p.author, { message: 'Author is required.' });
  maxLength(p.author, LIMITS.author, { message: `Author must be ${LIMITS.author} characters or fewer.` });
  required(p.text, { message: 'Text is required.' });
  maxLength(p.text, LIMITS.text, { message: `Text must be ${LIMITS.text} characters or fewer.` });
});

protected async onSubmit(): Promise<void> {
  await submit(this.quoteForm, {
    action: async (field) => {
      try {
        const quote = await firstValueFrom(this.quotesService.createQuote(field().value()));
        this.model.set({ author: '', text: '' });
        this.created.emit(quote);
      } catch (err) {
        if (err.status === 400 && err.error?.errors) {
          return Object.entries(err.error.errors).map(([fieldName, messages]) => ({
            fieldTree: this.quoteForm[fieldName], kind: 'server', message: messages[0],
          }));
        }
        this.serverError.set("Couldn't add the quote. Please try again.");
      }
    },
    onInvalid: () => {
      this.quoteForm().markAsTouched();
      if (this.quoteForm.author().invalid()) this.quoteForm.author().focusBoundControl();
      else if (this.quoteForm.text().invalid()) this.quoteForm.text().focusBoundControl();
    },
  });
  // + a second, guarded focusBoundControl() check for server-rejected fields - see verification-log.md
}
```
Template binds native `<input>`/`<textarea>` directly via `[formField]="quoteForm.author"` -
no custom control component needed.

## 3. Verification log

Full detail: [verification-log.md](verification-log.md). Screenshots:
[verification-screenshots/](verification-screenshots/) (including
`9-four-tabs-create-reactive.png` / `10-four-tabs-signal-forms.png` showing
both forms live in the same app, correctly highlighted tabs and all).
Summary: pristine, dirty, invalid (both variants), submitting,
server/network-error, server 400-field-error, and success all exercised
against the real running app; zero axe violations; correct keyboard tab
order. Also regression-checked: switching between the **Create** and
**Signal Forms** tabs repeatedly, submitting each independently, confirms
neither form's state leaks into the other and both keep working.

**The one concrete bug caught and fixed:** the first draft assumed
`submit()` gives touch-marking and focus-on-invalid "for free" the way the
hand-wired reactive-forms version did. It doesn't - and the missing
`novalidate` on the `<form>` compounded it: since `FormField` auto-applies
real native `required`/`maxlength` DOM attributes for the declared
validators, the *browser's own* HTML5 validation silently blocked the
`submit` event entirely before Angular ever saw it. Confirmed live: clicking
submit on an empty form showed no error, no `aria-invalid`, nothing.
Fixed by adding `novalidate` and an `onInvalid` handler
(`markAsTouched()` + `focusBoundControl()`). Re-verified: errors, aria
wiring, and focus all now correct on a failed submit.

## Signal Forms vs. reactive forms - comparison

**Simpler here:**
- **Character limit enforcement is free.** `maxLength()` in the schema
  auto-applies a real native `maxlength` DOM attribute (confirmed by reading
  the live DOM: `author-input.maxLength === 100`) - the reactive-forms
  version needed a hand-written `[attr.maxlength]="limits.author"` binding
  to get the same UX. Same for `required` - the native `required` attribute
  is set automatically.
- **No hand-rolled `submitting` signal.** Every field (and the root) has a
  built-in `submitting()` signal from the framework - the reactive version
  needed its own `signal(false)` toggled manually in `onSubmit()`.
- **Compile-time-checked field paths.** `p.author`/`p.text` are real
  TypeScript properties on the schema path, not magic strings like
  `formControlName="author"` - a typo or rename is caught by the compiler,
  not silently ignored at runtime.
- **`focusBoundControl()`** replaces the reactive version's manual
  `@ViewChild` + `.focus()` dance entirely - no template refs needed to move
  focus to a field.

**Still rough (the "preview" in the name is earned):**
- **Nothing is automatic on a failed submit.** `submit()`'s `action` only
  runs when the form is already valid; touch-marking and focus on an
  invalid attempt require an explicit `onInvalid` handler - genuinely easy
  to assume is built in (this session's actual bug), especially coming from
  a mental model where "the framework does more for me now."
- **`novalidate` matters more here, not less.** Because Signal Forms
  auto-applies real native validation attributes, forgetting `novalidate`
  silently hands control to the browser's own validation UI instead of the
  app's accessible one - a subtler trap than in reactive forms, where
  nothing native gets auto-applied in the first place.
- **Server-error-to-field wiring doesn't carry focus either.** The
  `fieldTree`-targeted error mechanism (`submit()`'s `action` returning
  `{fieldTree, kind, message}`) correctly attaches the error, but moving
  focus there needed the same manual `focusBoundControl()` call, checked
  *after* `submit()` resolves rather than inside the `action`'s `catch` -
  doing it inside `catch` raced the framework's own error-application, the
  same zoneless-timing trap as `day-14/piece1`'s original bug.
- **The server-error field mapping is still string-keyed** (`(this.quoteForm
  as any)[fieldName]`), so the compile-time safety win above doesn't extend
  to the one place field names come from outside the codebase (the API's
  400 response).

## GitHub

Pushed to the `thinkbridge-thinkschool` org repo - link to follow once
pushed (see Notes for mentor).

## Notes for mentor

- `day-14/piece1` (the reactive-forms version) is untouched and still
  serves as the comparison baseline. Piece2 additionally keeps its own copy
  of the reactive form on the **Create** tab (identical to piece1) alongside
  the new **Signal Forms** tab, so both are live in the same running app -
  not just described in a written comparison.
- `day-1/piece3/QuotesApi` was read-only reference; no source files there
  were modified.
- The real, installed `@angular/forms/signals` type declarations
  (`node_modules/@angular/forms/types/{signals,_structure-chunk}.d.ts`) were
  read directly before writing the brief or any code, since this is a fast-
  moving preview API.

## What did I learn this session

That a framework doing more for you by default (native `required`/
`maxlength` attributes, built-in `submitting()`) doesn't mean it does
*everything* for you - the exact places a reactive-forms implementation
needed explicit code (touch-marking and focus on a failed submit) are still
explicit here too, just moved into a differently-shaped hook (`onInvalid`
instead of an `if (form.invalid)` branch). The riskiest bugs in a new API
aren't where it's clearly manual or clearly automatic - they're in the
places that plausibly *could* be either, and Signal Forms' broader
automatic-attribute behavior makes assuming too much easier here than in
reactive forms, not harder.

## What would break this

See the "What breaks if the Week-1 API contract changes" section of
[verification-log.md](verification-log.md) - a field rename is now a
compile error (an improvement over reactive forms), but a new required
field or a tightened server-side limit break the same way they did before,
and the server-error-to-field mapping is still exposed to a silent
string-key mismatch.
