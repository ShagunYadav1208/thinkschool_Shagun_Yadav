# Brief to the agent (Claude Code)

**Target API (real, Week-1):** `QuotesApi` at [thinkschool_Shagun_Yadav/day-1/piece3/QuotesApi](../../day-1/piece3/QuotesApi)

- `GET /api/quotes/` - optional `page` (default 1) and `size` (default 10, max 100) query params.
  Returns a **plain JSON array**, no envelope/wrapper (no `totalCount`, no `items` property - just
  `[ {...}, {...} ]` directly).
- Quote JSON shape, exactly (`Models/Quote.cs`):
  ```json
  { "id": 1, "author": "Ada Lovelace", "text": "..." }
  ```
  **There is no `createdAt` field.** Several *other* Quotes APIs elsewhere in this repo (e.g.
  `day-3/piece6`, `day-5/piece4`) do have a timestamp field - this one specifically does not. Do not
  assume one exists.

**Goal:** a standalone Angular component (`quotes-feed` app) that:

1. Fetches the quote list from `GET /api/quotes/` via a small injectable service (`inject()`, not
   constructor injection).
2. Holds two signals: the fetched `quotes` list, and a user-typed `searchTerm` string.
3. Derives one `computed()` value from those two signals - the quotes whose `author` or `text`
   contains `searchTerm` (case-insensitive) - and renders *that* computed list with `@for`, `track
   quote.id`.
4. No `NgModule` anywhere. Angular's current default control flow (`@if`/`@for`/`@switch`), not the
   old `*ngIf`/`*ngFor` structural directives.
5. Zoneless change detection explicitly enabled in `app.config.ts` (`provideZonelessChangeDetection()`
   from `@angular/core`) - not just "standalone," actually zoneless, since that's the point of this
   exercise.
6. Handle three states explicitly in the template: loading, empty result (list loaded but zero
   matches), and populated - `@if`/`@else if`/`@else`, not silently rendering nothing.

Scaffold with the Angular CLI (`ng new --standalone`), write the service and component by hand on top
of that scaffold, and leave the generated `app.spec.ts`/testing setup as the CLI produced it unless it
needs a real change to keep compiling.
