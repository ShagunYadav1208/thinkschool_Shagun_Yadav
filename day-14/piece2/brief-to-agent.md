# Brief to the agent (Claude Code) - Signal Forms rebuild

**Target endpoint (real, Week-1, unchanged from Day 14/piece1):** `POST /api/quotes/`
on `QuotesApi` at [thinkschool_Shagun_Yadav/day-1/piece3/QuotesApi](../../day-1/piece3/QuotesApi).

- Request body, exactly (`Models/CreateQuoteRequest.cs`): `{ "author": string, "text": string }`.
  No other fields.
- `author`: required, max **100** characters. `text`: required, max **1000** characters.
  Confirmed live against the running API - same limits as the reactive-forms version in
  `day-14/piece1`.
- Success: `201 Created`, body `{ "id": number, "author": string, "text": string }`.
- Validation failure: `400`, body `{ "errors": { "author": ["Author is required."] } }` -
  keyed by lowercase field name.

**Goal:** rebuild the exact same create-a-quote form from `day-14/piece1`, but using the
**Signal Forms preview API** (`@angular/forms/signals`, real package installed at
`@angular/forms@22.1.3` in this project - confirmed by reading its actual `.d.ts` before writing
any code, not from memory/guesswork) instead of `ReactiveFormsModule`. This goes in
`day-14/piece2/quotes-list-detail` - a copy of the piece1 app, so piece1 stays untouched as the
reactive-forms reference to compare against.

1. Use `form()` from `@angular/forms/signals` to wrap a `signal({ author: '', text: '' })` model,
   with a schema function binding `required()` and `maxLength()` validators to `author`/`text`
   matching the real 100/1000 limits above.
2. Bind the native `<input>`/`<textarea>` to their fields with the `[formField]` directive
   (`FormField`, imported from `@angular/forms/signals`) - not custom form controls.
3. Same UI/behavior contract as the reactive-forms version: errors shown only once
   touched/dirty; `aria-invalid`/`aria-describedby` on invalid fields; associated
   `<label for>`; keyboard-operable; focus moves to the first invalid control on a failed
   submit attempt; states for empty/pristine, invalid, submitting, server-error, and success
   (new quote added to the existing list, no reload, reusing the existing `QuotesService`).
4. Use `submit()` from `@angular/forms/signals` to run the real `createQuote()` call as the
   form's `action`, and map the API's real `400` `{ errors: { author: [...] } }` shape onto
   the matching field via the submission error's `fieldTree` (per the `submit()` example in
   the package's own type declarations) - not a hand-rolled error dictionary.

**Do not assume Signal Forms handles accessibility or touched-marking "for free" just because
it's a newer/preview API** - verify what the `FormField` directive and `submit()` actually do
(read the type declarations / actual behavior) rather than assuming parity with what a
polished, hand-wired reactive-forms version already does. Verify with the keyboard and axe,
not just by eyeballing the markup, exactly like day-14/piece1's verification did.
