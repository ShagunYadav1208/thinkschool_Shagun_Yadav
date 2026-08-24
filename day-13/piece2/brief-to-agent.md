# Brief to the agent (Claude Code)

**Target API (real, Week-1):** `QuotesApi` at [thinkschool_Shagun_Yadav/day-1/piece3/QuotesApi](../../day-1/piece3/QuotesApi)

- `GET /api/quotes/` - list. Plain JSON array, no envelope: `[{ "id": 1, "author": "...", "text": "..." }, ...]`.
- `GET /api/quotes/{id}` - detail. `200` with a single quote object (same shape as above) if it
  exists, `404` **with an empty response body** if it doesn't - don't assume there's a JSON error
  object to read a message out of on failure, because there isn't one.
- Quote fields, exactly: `id` (number), `author` (string), `text` (string). No `createdAt`, no other
  fields - confirmed live against the running API, not guessed.

**Goal:** a list+detail component:

1. Left/top: the quote list from `GET /api/quotes/`. Clicking a quote selects it.
2. Right/bottom: the selected quote's detail, fetched from `GET /api/quotes/{id}` when a quote is
   clicked - not just re-displaying data already in the list, actually issue the detail request.
3. Signals for `loading`/`error`/data, separately for the list and for the detail pane (list loading
   and detail loading are different things and should be able to be true independently).
4. `inject()` for the service, not constructor injection.
5. The `Quote` model fully typed - no `any` anywhere in the data path (fetch, service, component).
6. **Handle the stale-response race**: if a user clicks quote A, then clicks quote B before A's
   detail request has resolved, and A's response arrives *after* B's does, the detail pane must end
   up showing B - not have A's late response silently overwrite B's. This needs to actually be
   verified with a real interleaving, not just asserted in a comment.
7. Explicit states in the template: list loading, list error, list empty (zero quotes), detail
   loading, detail error (including a 404 - "not found" is a real, reachable state here, not a
   hypothetical), and no quote selected yet.

Scaffold with the Angular CLI (`ng new --standalone`), zoneless
(`provideZonelessChangeDetection()`), write the service and components by hand on top of that
scaffold.
