# Design review — ADR-001

Two critiques on the first draft of [ADR-001](ADR-001-reservation-ownership-authorization.md),
one mentor-level, one peer-level, both of which changed the final document.

## Mentor critique — the header option was a placebo, not a step

**What they said:** the first draft's options table listed "read the caller's `UserId` from an
`X-User-Id` header, trust it" as a viable *interim* rollout step, to be replaced by the JWT scheme
later. That's not an interim control, it's the appearance of one — any caller can set that header
to any GUID, so the ownership check being added would pass for an attacker who does nothing more
than copy the shape of the request they're trying to forge. It's worse than doing nothing: once a
`SecurityHardeningTests`-style suite goes green against it, the tests actively lie about what's
protected, and the next person reading this ADR would reasonably assume the gap is closed when
it isn't. If a real per-user credential isn't ready yet, the finding should stay open and say so —
spending engineering time on a control that only looks like a fix is worse than spending no time at
all.

**How it changed the design:** the header option was removed from "staged rollout" framing entirely
and reclassified in ADR-001 as **rejected outright** with the reasoning stated plainly, not left as
a softer "not chosen this pass." The decision moved straight from "current gap" to "minimal JWT,"
with no header-based middle step presented as acceptable even temporarily.

## Peer critique — why two auth schemes instead of one?

**What they said:** a fellow student who'd been following the `day-25`/`QuotesApi` Entra ID track
pointed out that a real bearer token already carries a `sub` claim that could double as both "which
client" and "which user" — Entra ID app registrations have a client ID too. If ParkFlow is adding a
JWT anyway, why keep the API key around instead of letting one scheme answer both questions?

**How it changed the design:** the decision itself didn't change — both schemes are still kept —
but the ADR was rewritten to state *why* explicitly rather than leaving it implicit. The API key
answers "which client application is calling" (and is what the Day 27 rate limiter is already keyed
on); the JWT answers "which user is that client acting on behalf of." Those collapse into the same
answer in a single client, single tenant setup, which is exactly why the distinction is easy to miss
— but a future integration (e.g., a facility-operator dashboard acting on behalf of many drivers)
needs the split to exist. The critique didn't move the decision, but it forced the reasoning onto
the page instead of leaving a reviewer to wonder why the design didn't just simplify to one scheme.

## Self-review finding, folded in during the same pass

Re-reading [`Result.cs`](reference/Result.cs) while responding to the above turned up a third
issue that wasn't part of either critique but belongs in the same fix: `Result` only carries
`IsSuccess` and a string `Error`. Today, `ReservationApplicationService.CancelAsync`'s "reservation
not found" and the new "not your reservation" outcome would both flow into
[`ReservationsController`](reference/ReservationsController.cs)'s existing `BadRequest(...)`
branch — a 403 and a 404 both rendering as 400. That's not acceptable for a fix whose entire point
is making the forbidden case distinguishable and enforceable, so extending `Result` with an
error-kind became Build Day 29's first task rather than an afterthought discovered mid-implementation.

## Net effect

The critique didn't change *which* decision ADR-001 makes — the minimal-JWT-alongside-API-key
outcome was the strongest option in the first draft too. What it changed was rigor: a fake-looking
interim step got cut before it could ship, the reasoning for a design choice that looked redundant
got written down instead of left implicit, and a real gap in the supporting `Result` type got
caught before implementation instead of during it.
