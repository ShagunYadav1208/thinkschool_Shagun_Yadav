# Day 30 / Piece 1 — Build Day 2: feature completeness (ownership enforcement)

Implements Build Day 30 from [day-28/piece1/BUILD-PLAN.md](../../day-28/piece1/BUILD-PLAN.md) —
the second of four build days implementing
[ADR-001](../../day-28/piece1/ADR-001-reservation-ownership-authorization.md). Built on top of
[day-29/piece1/ParkFlow](../../day-29/piece1/ParkFlow) — copied in and extended per this piece's
brief; day-22/piece1, day-27/piece1, day-28/piece1, and day-29/piece1 are all untouched.

Day 29 built the identity plumbing (JWT scheme, composite auth policy) but wired no ownership
check — any valid demo user's token cleared the gate. Today closes the actual gap: `Cancel`,
`CheckIn`, and `CompleteAsync` now check `reservation.UserId == currentUserId` before touching the
aggregate, and the controller maps `Result`'s error kind to the right status code instead of a
single `BadRequest`.

## Layout

```
ParkFlow/    - day-29/piece1's ParkFlow, copied here and extended (see below)
```

## What changed

- **`ReservationApplicationService.CancelAsync` / `CheckInAsync` / `CompleteAsync`** each gain a
  `currentUserId` parameter. The ownership guard runs *before* the aggregate is touched — a
  non-owner gets `Forbidden` regardless of the reservation's actual state, so probing a reservation
  as the wrong user can't leak anything about its status. "Reservation not found" now returns
  `Result.NotFound(...)` instead of the generic `Failure(...)` Day 29 left in place.
- **`ReservationsController`** reads `User.FindFirstValue(ClaimTypes.NameIdentifier)` (guaranteed
  present — the composite policy already required a validated Bearer token) and passes it through.
  A new `ToActionResult` helper maps `ResultErrorKind`: `NotFound` → 404, `Forbidden` → 403,
  everything else → 400 — replacing the single `BadRequest(...)` branch every failure used to hit.
- **Tests**: `ReservationOwnershipTests.cs` (new, 6 tests) — owner succeeds, a different user gets
  403 on all three actions (including check-in/complete on a *Pending* reservation, proving the
  guard runs before the domain state check would otherwise 400), a nonexistent reservation is 404
  not 403, and a second cancel attempt still falls through to 400. `ReservationMutationAuthTests.cs`
  (carried over from Day 29) now creates its reservations owned by `userA` specifically so its
  auth-layer assertions aren't accidentally exercising the new ownership guard.

## Curl walkthrough (happy path + the actual BOLA fix, against the real running API)

```
$ curl -X POST .../reservations -H "X-Api-Key: $KEY" -d '{"userId":"<userA>", ...}'
{"reservationId":"328781be-8993-4fb9-a472-fb6d21d19b80"}

$ curl -X POST .../dev/token -d '{"user":"userB"}'          # the attacker's own valid token
{"token":"eyJ..."}

$ curl -X POST .../reservations/<id>/cancel -H "X-Api-Key: $KEY" -H "Authorization: Bearer $TOKEN_B"
{"error":"You do not own this reservation."}
status=403

$ curl -X POST .../reservations/<id>/check-in -H "X-Api-Key: $KEY" -H "Authorization: Bearer $TOKEN_B"
{"error":"You do not own this reservation."}
status=403                                                   # not 400 — guard ran before the state check

$ curl -X POST .../reservations/<id>/cancel -H "X-Api-Key: $KEY" -H "Authorization: Bearer $TOKEN_A"
status=204                                                   # the actual owner succeeds

$ curl -X POST .../reservations/<nonexistent-id>/cancel -H "X-Api-Key: $KEY" -H "Authorization: Bearer $TOKEN_A"
{"error":"Reservation not found."}
status=404
```

`dotnet build ParkFlow.slnx`: 0 warnings, 0 errors. `dotnet test ParkFlow.slnx`: **50/50 passing**
(day-29's 44 + 6 new `ReservationOwnershipTests`), 0 regressions.

## PR / review

Per this piece's brief ("open a PR for review; respond to comments like you would on a team"), this
went up as a real PR rather than a direct push to `main` — see the GitHub link section for the PR
URL and the review-comment thread once posted. This session doesn't have `gh`/API credentials
available to push the PR + comment round-trip through in one pass, so the exact mechanics (branch
name, PR number, thread link) are recorded there once resolved with the user, not invented ahead of
time.

## What I learned this session

Writing `ToActionResult` as a `switch` on `ResultErrorKind` rather than another `if (result.IsSuccess)
... else` chain is a small thing, but it's what actually made the "which failures map to which
status code" question visible as a single, exhaustive place to look — Day 29's `Result` shape work
would have been half-finished if the controller still only knew about two cases. The more
interesting thing was designing the "wrong user" test for check-in/complete: I initially reached for
a Confirmed-state reservation so the test would look like a "real" check-in attempt, then realized
the *more* important proof is that the guard rejects a non-owner on a Pending reservation too — an
attacker shouldn't be able to distinguish "not yours" from "not in the right state" by which error
code comes back.

## What would break this

| Failure | Status |
|---|---|
| User B cancels/checks-in/completes User A's reservation | **Fixed** — `ReservationOwnershipTests.cs` proves 403 on all three actions. |
| A non-owner learns the reservation's state by the error they get back | Avoided — the ownership guard runs before the aggregate is touched, so a non-owner always gets 403, never a state-specific 400. |
| A 403 and a 404 both reported as one generic 400 | **Fixed** — `ToActionResult` maps `ResultErrorKind` explicitly. |
| The demo token's `sub` claim missing or malformed when `GetCurrentUserId()` parses it | Not defended against directly — relies on `ReservationMutationPolicy` having already required a validated Bearer token first. A real IdP (Build Day 32) would make this the same invariant it already is for `ClaimTypes.NameIdentifier` generally, but a malformed-but-signed token from a future issuer that omits `sub` isn't covered by a test yet. |
| Regression pass / ZAP rescan against the dual-auth + ownership posture | Not this piece — Build Day 31. |

## GitHub link

_To be filled in once the PR and branch are confirmed — see the note under "PR / review" above._

## Notes for mentor

- `cd day-30/piece1/ParkFlow && dotnet build ParkFlow.slnx` — 0 warnings, 0 errors, same 20 projects
  as day-29/piece1.
- `dotnet test ParkFlow.slnx` — 50/50 passing.
- The curl walkthrough above was run against a real `dotnet run` instance, not just asserted through
  the test suite.
- Deliberately kept the ownership guard as a plain field comparison (`reservation.UserId ==
  currentUserId`) rather than pushing it into the aggregate itself — `Reservation` has no notion of
  "who's asking" anywhere else, and ADR-001 frames this as an authorization concern, not a domain
  rule, so it belongs in the application service that already knows about `Result`/`Forbidden`.
