# Brief to the agent (Claude Code)

**Target endpoint (real, Week-1):** `POST /api/quotes/` on `QuotesApi` at
[thinkschool_Shagun_Yadav/day-1/piece3/QuotesApi](../../day-1/piece3/QuotesApi)

- Request body, exactly (`Models/CreateQuoteRequest.cs`): `{ "author": string, "text": string }`.
  **No other fields** - don't add `title`, `category`, `tags`, or anything else invented.
- Constraints, confirmed live against the running API (not guessed from the model annotations alone):
  - `author`: required (whitespace-only counts as empty), max **100** characters.
  - `text`: required (whitespace-only counts as empty), max **1000** characters.
- Success: `201 Created`, body `{ "id": number, "author": string, "text": string }`.
- Validation failure: `400`, body shaped like
  `{ "errors": { "author": ["Author is required."] } }` or
  `{ "errors": { "text": ["Text must be 1000 characters or fewer."] } }` - keyed by lowercase field
  name, one message array per field. Confirmed live: sending an empty author, a 101-character
  author, and a 1001-character text each produced exactly one of these two field keys, never both
  invented ones like `authorName` or `quoteText`.

**Goal:** a reactive create-a-quote form, added into the **existing, already-running app**
(`day-13/piece2/quotes-list-detail`) - not a new standalone app on a new port. The form should sit
above or alongside the existing list+detail view in the same component tree.

1. `FormGroup`/`FormControl` (Angular's `ReactiveFormsModule`) with `author` and `text` controls,
   validators matching the real limits above exactly - not guessed round numbers.
2. Error messages shown per field, only once the field has been touched/interacted with (not
   immediately on page load).
3. Full accessibility: every input has an associated `<label for="...">`; every invalid control gets
   `aria-invalid="true"` and `aria-describedby` pointing at the id of the element that actually
   contains its error text (not a dangling id); the whole form is operable by keyboard alone (native
   `<form>`/`<input>`/`<button>`, no click-only div handlers); on a failed submit attempt, focus moves
   to the first invalid control.
4. States: empty (untouched), invalid (client-side, before any request), submitting (request in
   flight, submit disabled), server-error (the request itself fails - network error or an unexpected
   non-2xx/non-400 status), and success (adds the new quote to the existing list without a full page
   reload).
5. On success, the new quote should actually show up in the existing `quotes` list/search/dropdown
   without a manual refresh - reuse the existing `QuotesService`, don't build a second copy of it.

Verify with the keyboard (tab order, focus-on-error) and with automated accessibility checks (axe or
equivalent), not just by eyeballing the markup.
