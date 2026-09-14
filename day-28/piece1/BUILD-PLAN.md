# Build plan — implementing ADR-001

Day 28 is design/review only — nothing in `day-22/piece2` or `day-27/piece1` is touched by this
piece. This is the plan for the build days that would actually implement
[ADR-001](ADR-001-reservation-ownership-authorization.md), continuing the capstone's existing
day-by-day cadence.

## Day 29 — Identity plumbing, no behavior change yet

- Add a JWT bearer scheme to `ParkFlow.Api` (`Microsoft.AspNetCore.Authentication.JwtBearer`)
  validating a `sub` claim as `UserId`. `Development`-only token issuance endpoint, seeded with
  known demo user IDs — fails closed outside `Development`, mirroring
  [`ApiKeyAuthenticationHandler`](reference/ApiKeyAuthenticationHandler.cs)'s existing posture.
- Add a composite `[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]` policy for the three
  reservation-mutation routes only — `Create` and read routes keep today's API-key-only
  requirement, since they don't need an ownership check.
- Extend [`Result`](reference/Result.cs) with an error kind (`NotFound` / `Forbidden` /
  `Validation`) instead of a bare string, per the gap DESIGN-REVIEW.md surfaced. Unit tests for the
  new shape only — no controller changes yet.
- No ownership check wired up yet. Goal for the day: build clean, existing 20/20 tests still pass,
  new scheme provably rejects requests with no/invalid JWT on the three routes (still 403/401 with
  no ownership logic behind it).

## Day 30 — Ownership enforcement

- `ReservationApplicationService.CancelAsync` / `CheckInAsync` / `CompleteAsync` gain a
  `currentUserId` parameter; each checks `reservation.UserId == currentUserId` before calling the
  aggregate, returning the new `Result.Forbidden(...)` otherwise.
- `ReservationsController` reads `User.FindFirstValue(ClaimTypes.NameIdentifier)` and passes it
  through; maps `Forbidden` → 403, `NotFound` → 404, `Validation` → 400 (previously all three were
  a single `BadRequest`).
- New integration tests, same style as Day 27's `SecurityHardeningTests.cs`: user A creates a
  reservation; user B's JWT attempting cancel/check-in/complete gets 403; user A's own JWT gets the
  original 204/201. Target: the existing 20 plus these new ones, 0 regressions.

## Day 31 — Regression pass and threat-model close-out

- Full suite: `dotnet test ParkFlow.slnx` — 0 regressions, `dotnet build` still 0 warnings.
- Re-run the OWASP ZAP baseline scan (per `day-27/piece1/SECURITY-TESTING.md`'s method) against the
  dual-auth posture to confirm the added bearer-token handling introduces no new passive findings.
- Update the threat model's BOLA finding from "open" to "closed — pending a real IdP for the
  token-issuance endpoint," once the above is real and tested, not before.

## Day 32 (stretch) — swap the dev token issuer for a real IdP

- Point the JWT bearer options at Entra ID (new app registration scoped to ParkFlow, not a reuse of
  `day-25`'s `QuotesApi` registration) instead of the dev-token endpoint.
- Because the ownership check only ever depends on the `sub` claim, this is a configuration change
  (`Authority`, `Audience`) — no change to `ReservationApplicationService` or the controller. This
  is the forward-compatibility ADR-001 was chosen for; Day 32 is where that claim gets proven true
  or false.
