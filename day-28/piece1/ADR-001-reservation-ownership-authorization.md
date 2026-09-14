# ADR-001: How ParkFlow closes the reservation-ownership (BOLA) gap

## Status

Accepted (for capstone/course scope — see "Not production-ready" under Consequences).

## Context

[THREAT-MODEL.md §3.1](reference/THREAT-MODEL.md) ranks this the #1 finding across the whole
capstone, and day-27's README flagged it as "the one thing I'd flag hardest for review":
`CancelAsync`, `CheckInAsync`, and `CompleteAsync` in
[`ReservationApplicationService`](reference/ReservationApplicationService.cs) take only a
`reservationId` — there is no check that the caller owns that reservation. Anyone who can guess or
observe a GUID can cancel, check in, or complete *any other user's* booking.

Day 27 added [`ApiKeyAuthenticationHandler`](reference/ApiKeyAuthenticationHandler.cs), which
authenticates a request as coming from a trusted **client of the API** (a single shared key). It
was explicitly documented as not closing this gap, because it carries no notion of *which end
user* is calling — there's no `UserId` claim anywhere on the request to check the reservation
against.

This is the one decision that matters most for the capstone right now: every other Day 27 finding
(input validation, rate limiting, headers, private data-tier networking) is either fully fixed or a
straightforward follow-on. This one requires an actual architectural choice about how ParkFlow
represents "who is the calling user," and that choice has to interact correctly with:

- The existing API-key scheme (should it be replaced, kept alongside, or removed?).
- The sibling course track's Entra ID work (`day-25/piece1`'s `QuotesApi`), which solves user
  identity for a *different* app and must not be blindly copy-pasted onto a different codebase
  just because it exists.
- The capstone's time-box — a full production-grade identity provider is out of scope for a
  student project, but a control that only *looks* fixed is worse than an honestly-documented gap.

## Decision drivers

1. **Real security, not the appearance of it.** Whatever ships must actually stop user B from
   acting on user A's reservation — not just add a field that a client can set to any value.
2. **Reversibility toward a real IdP.** ParkFlow will eventually want a proper identity provider
   (Entra ID or otherwise). The fix chosen now shouldn't need to be rewritten when that happens —
   only reconfigured.
3. **Scope discipline.** Don't duplicate `day-25`'s full Entra ID app-registration setup onto a
   different app just because it's available; that's real engineering cost this pass doesn't need
   to spend (per THREAT-MODEL.md §5).
4. **Testability.** The fix needs the same kind of proof Day 27's `SecurityHardeningTests.cs`
   gave the API-key work — a 403 test, not just a code review claim.

## Considered options

| # | Option | Verdict |
|---|---|---|
| A | Leave it documented, fix later | Rejected — see "What would break this" in the Day 27 README; a capstone review day is exactly the moment to close a ranked #1 finding, not defer it again. |
| B | A caller-supplied `X-User-Id` header, trusted as-is | **Rejected outright, not staged as an interim step** — see [DESIGN-REVIEW.md](DESIGN-REVIEW.md) for why an earlier draft of this ADR treated this as a viable rollout step and the mentor critique that removed it. Any caller can set this header to any GUID; it doesn't authenticate anything, it just moves the spoofing surface from the URL into a header. |
| C | Full Entra ID / OAuth2 integration, mirroring `day-25`'s `QuotesApi` setup | Rejected for *this* pass — real, standards-based, but requires a new App Registration and is the larger lift THREAT-MODEL.md §5 already declined to take on for ParkFlow specifically. Kept as the natural next step (see Build Day 32 in [BUILD-PLAN.md](BUILD-PLAN.md)), not as this decision. |
| D | A minimal per-user JWT bearer scheme (course-issued for now, a `sub` claim = `UserId`), added *alongside* the existing API-key scheme, plus an explicit ownership check in the three vulnerable methods | **Chosen.** |

## Decision

Adopt **Option D**. Add a JWT bearer authentication scheme to `ParkFlow.Api` that validates a
`sub` claim as the caller's `UserId`, and require both schemes on the reservation-mutation routes:
the API key answers "which client application is calling," the JWT answers "on behalf of which
user." `ReservationApplicationService.CancelAsync` / `CheckInAsync` / `CompleteAsync` each gain a
`currentUserId` parameter and a guard — `reservation.UserId == currentUserId`, else a `Forbidden`
result — before touching the aggregate.

The two schemes are kept **both**, not one instead of the other, because they answer genuinely
different questions that only look redundant in a single-tenant setup: the API key is what the
Day 27 rate limiter is already keyed on and is what would let ParkFlow later revoke one integration
partner's access without touching any end user's session; the JWT is what lets the ownership check
work at all. A future integration (e.g. a facility-operator dashboard acting on behalf of many
drivers) needs exactly this split — one client identity, many user identities behind it. This
reasoning was sharpened directly by peer feedback during review — see DESIGN-REVIEW.md.

For course scope, JWTs are minted by a `Development`-only token endpoint seeded with known demo
`UserId`s (mirrors the "fails closed outside Development" posture the API key already uses) — not
a real identity provider. Swapping the token issuer for Entra ID later is a configuration change
(`Authority`, `Audience`) because the ownership check only ever depends on the `ClaimsPrincipal`
having a `sub`/`NameIdentifier` claim, never on how that claim was minted.

## Consequences

**Positive**

- Closes the #1 threat-model finding with an enforceable check, not just a header.
- Forward-compatible with a real IdP by construction (config change, not a redesign).
- Keeps the API-key/JWT split legible for a future multi-tenant integration, instead of collapsing
  "which app" and "which user" into one concept.

**Negative / accepted risk**

- Two authentication schemes on one API is genuinely more moving parts than one; the composite
  `[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]` policy needs its own test coverage so the
  "and" isn't accidentally an "or."
- The `Result` type ([`reference/Result.cs`](reference/Result.cs)) currently only carries
  `IsSuccess` + a string `Error` — a `Forbidden` outcome and a `NotFound` outcome both collapse
  into the controller's existing `BadRequest(...)` branch today. This was surfaced during review
  (see DESIGN-REVIEW.md) and is folded into Build Day 29 rather than left implicit: `Result` needs
  an error-kind so 403 and 404 stop being indistinguishable from 400.
- **Not production-ready as written**: the `Development`-only token endpoint is a stand-in for a
  real IdP, seeded with known demo user IDs. It must never be exposed outside `Development` — the
  same fail-closed posture Day 27 already applies to the API key needs to apply here too, and is
  called out explicitly so it isn't mistaken for a finished identity system.

## Links

- [THREAT-MODEL.md §3.1, §4, §5](reference/THREAT-MODEL.md) — the finding this ADR closes, and why
  Entra ID wasn't chosen for ParkFlow in the prior pass.
- [ReservationApplicationService.cs](reference/ReservationApplicationService.cs),
  [ReservationsController.cs](reference/ReservationsController.cs) — the three vulnerable methods.
- [ApiKeyAuthenticationHandler.cs](reference/ApiKeyAuthenticationHandler.cs) — the existing scheme
  this one is added alongside, not instead of.
- [BUILD-PLAN.md](BUILD-PLAN.md) — day-by-day implementation of this decision.
- [DESIGN-REVIEW.md](DESIGN-REVIEW.md) — the critique that shaped the final form of this ADR.
