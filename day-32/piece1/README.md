# Day 32 / Piece 1 — Ship + demo + postmortem (+ a real frontend)

Closes out the ADR-001 arc (day-28/piece1) that ran days 27–32: ship the reservation-ownership fix
live, demo it against the real deployment, and write the postmortem — then, on request, build an
actual browser frontend for the live site (book a spot, run it through its lifecycle, see the
charge) rather than leaving the API as the only way to touch it. Built on top of
[day-31/piece1/ParkFlow](../../day-31/piece1/ParkFlow) — copied in, then genuinely extended here
(new endpoints, seed data, the frontend itself); day-22, day-27, day-28, day-29, day-30, and day-31
are all untouched.

## Layout

```
ParkFlow/          - day-31/piece1's ParkFlow, copied here and extended (see "What's new" below)
demo/index.html    - archived copy of the published request/response demo page (also live)
POSTMORTEM.md      - the one-page postmortem
```

## Live URL

**https://parkflow-demo-20667.azurewebsites.net** — open it in a browser; it's a real page now; a
sign-in selector, a spot grid, a booking form, and a reservations list with running charges.

Real Azure App Service deployment (Linux, `.NET 10`, Free/F1 tier, Central India region — the only
region this subscription's policy allowed for new resources; `eastus`/`westus2`/`northeurope`/
`westeurope`/`uksouth`/`southeastasia` were all rejected with `RequestDisallowedByAzure` before
`centralindia` worked). Resource group `parkflow-demo-rg`, App Service plan `parkflow-demo-plan`,
web app `parkflow-demo-20667` — created and deployed via `az cli` (`az group create`,
`az appservice plan create`, `az webapp create`, `dotnet publish` + `az webapp deploy --type zip`),
not clicked through the portal.

`GET /health` and `GET /openapi/v1.json` are still live and anonymous too, same as before.

## What's new: a real frontend (`wwwroot/`)

The brief was: book a parking spot, release it, see the charge, and "whatever else should be
there." What that actually required, since the API only ever had write endpoints plus one
availability-count read:

- **`wwwroot/index.html` + `app.js` + `style.css`** — sign in as a demo user, register a vehicle,
  see a live spot grid (color-coded by real status), book a spot, and walk a reservation through
  Confirm → Check-in → Complete (charge finalized, spot released) or Cancel, with running totals
  (active bookings, completed trips, total charged). No framework, no build step — served as
  static files from the same app.
- **New backend, all in this piece's own `ParkFlow/` copy**: `GET /api/v1/parking/facilities`,
  `GET /api/v1/parking/facilities/{id}/spots`, `POST /api/v1/parking/spots/{id}/reserve`,
  `POST /api/v1/parking/spots/{id}/occupy` (the application-layer methods already existed —
  `ParkingSpotApplicationService`'s own doc comment calls them "scaffolding" for a message-broker
  consumer that doesn't exist yet — they just had no controller action before this piece);
  `POST /api/v1/reservations/{id}/confirm` and `GET /api/v1/reservations/mine` (new
  `ReservationApplicationService` methods — `Confirm` was never callable over HTTP before this
  piece, which meant `CheckIn`/`Complete` were dead ends too, since `CheckIn()` requires
  `Confirmed`). Ownership-checked the same way as Cancel/CheckIn/Complete (ADR-001).
- **Seed data** — one facility, nine spots, created once at startup if the store is empty. The
  InMemory provider (day-22/piece1) starts empty on every run and there was still no
  facility/spot-creation endpoint; without this the frontend would have nothing to show.
- **11 new integration tests** (`ParkingDirectoryTests.cs`, `ReservationConfirmAndMineTests.cs`) —
  65/65 passing total (day-31's 54 + these 11), 0 regressions.

### Two real bugs a browser caught that no test did

Every existing test in this project talks to the API directly — none of them render HTML, so
neither ever exercised what a browser actually does with this app. Loading the page for real (via
Playwright, headless Chromium — screenshots and full click-through, not just `curl`) found two bugs
in the first minute that would have shipped invisibly otherwise:

1. **The CSP blocked the frontend from loading at all.** Day 27's `Content-Security-Policy:
   default-src 'none'` was written on the explicit reasoning "a JSON API serves no HTML" — true
   until this piece added one. `'none'` silently blocked this page's own script, its own
   stylesheet, and the Google Fonts stylesheet it loads. Fixed by scoping the CSP to `'self'` plus
   the two Google Fonts hosts the page explicitly opts into (`SecurityHeadersMiddleware.cs`) —
   still far narrower than a typical app's CSP, just no longer broken for same-origin content.
2. **A hardcoded API key only worked in one environment.** The first version of `app.js` embedded
   the live deployment's API key directly — which then 401'd every request when tested locally
   against the dev key. Fixed properly, not papered over: `GET /api/v1/client-config` (new,
   anonymous) echoes back whatever `Security:ApiKey` *this* environment is actually configured
   with, so the identical shipped JS works unmodified in `Development` and against the live demo.
   This doesn't weaken the key as a control — anyone who could read it out of `app.js`'s source
   could equally have called that endpoint directly; it just stops one environment's secret from
   being baked into a file every environment serves.

A third, unrelated bug surfaced deploying the fix: PowerShell's `Compress-Archive` writes zip entry
names with backslashes (`wwwroot\index.html`) instead of the forward slashes the zip spec requires,
which crashed Kudu's Linux-side extraction with `Invalid argument`. Rebuilding the zip with Python's
`zipfile` (which writes spec-correct paths) fixed it — `az webapp deploy` was never the problem.

### The one deliberate tradeoff: this instance runs in `Development`

`ASPNETCORE_ENVIRONMENT=Development` on the live instance — specifically so `POST /api/v1/dev/token`
answers and the frontend's sign-in selector (and the ownership demo below) actually work against
the real URL. Every other environment this app has run in this capstone (the ZAP scan target in
day-31/piece1, every local `Production`-postured test) has that endpoint fail closed with a genuine
404. This is a demo-only choice, made once, stated once, not something a real launch would ever do
— see `POSTMORTEM.md`'s "what would break this" for the full accounting. The API key and JWT
signing key are freshly generated for this deployment specifically; neither is the
`dev-local-only-*` placeholder from any other piece's `appsettings.Development.json`, and neither
is written to any file in this repo — the frontend fetches the key at runtime (see above) rather
than the repo ever containing it.

## Demo

**https://claude.ai/artifact/5NyeCym93VVyMWUvoje4ke** (archived copy: [`demo/index.html`](demo/index.html))

A request-by-request walkthrough of the ownership check specifically — every call in it ran against
the live URL, in that order: no auth (401) → userA creates a reservation (201) → mint real tokens
for userA and userB → userB, a genuine stranger to that reservation, gets 403 → userA cancels it
(204) → cancelling again is a business-state 400, not an auth failure → a reservation that never
existed is a 404, distinct from the 403. That sequence is the entire point of the four days before
this one. The live URL above is now the fuller demo — the artifact is the focused proof of the one
thing this whole arc was actually about.

## Postmortem

[`POSTMORTEM.md`](POSTMORTEM.md) — one page: what I'd do differently (stand up the deployment path
on day 27, not day 32), the hardest bug (day 29's `AuthenticationTypes.Federation` mistagging), and
the thing I'm proudest of (catching that BUILD-PLAN.md's own literal instruction for the composite
auth policy was an OR, not an AND — before it shipped, with a test).

## What I learned this session

The postmortem covers the ADR-001 arc itself; the frontend add-on taught a narrower, sharper
version of the same lesson: every one of this project's 54 existing tests passed the whole time the
CSP bug and the hardcoded-key bug were live, because none of them render a page in a browser. Test
coverage that never exercises the actual client surface can be 100% green and still ship a frontend
that doesn't load at all — the two bugs here weren't found by more tests, they were found by
looking at the thing.

## What would break this

See `POSTMORTEM.md`'s own table for the ADR-001-arc risks (still current). Specific to the
frontend: the API key is fetched at runtime but still visible to anyone who opens dev tools —
acceptable for a synthetic-data demo, never for anything real; the seed data (one facility, nine
spots) resets on every restart along with everything else (EF Core InMemory, by design); and the
frontend has no pagination or facility switcher — it assumes exactly the one seeded facility, which
is true today and would need work the moment a second one existed.

## GitHub link

_To be filled in once pushed — following this piece's PR/branch conventions from Days 30–31._

## Notes for mentor

- The live URL, the deploy commands, the two browser-only bugs, and the zip-deploy fix are all
  real — verified with actual Playwright runs (headless Chromium, screenshots taken) against both a
  local instance and the live URL, not just asserted.
- `cd day-32/piece1/ParkFlow && dotnet build ParkFlow.slnx` — 0 warnings, 0 errors, same 21 projects
  as day-31. `dotnet test ParkFlow.slnx` — **65/65 passing** (day-31's 54 + 11 new).
- I'd flag two things for a second opinion: the `Development`-on-a-public-URL tradeoff (same as
  before this frontend existed), and the runtime-fetched-but-still-client-visible API key pattern
  for the frontend — I think both are correctly scoped to "synthetic demo, no real data," but
  they're exactly the kind of thing that shouldn't survive contact with a real deployment unreviewed.
