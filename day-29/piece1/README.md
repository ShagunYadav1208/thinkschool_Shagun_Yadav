# Day 29 / Piece 1 — Build Day 1: identity plumbing, no behavior change yet

Implements Build Day 29 from [day-28/piece1/BUILD-PLAN.md](../../day-28/piece1/BUILD-PLAN.md), the
first of four build days that implement
[ADR-001](../../day-28/piece1/ADR-001-reservation-ownership-authorization.md) (closing the
reservation-ownership/BOLA gap THREAT-MODEL.md ranked #1). Built on top of
[day-27/piece1/ParkFlow](../../day-27/piece1/ParkFlow) — copied in and extended per this piece's
brief; day-27/piece1 itself is untouched.

Per the plan for today: add the identity plumbing (JWT bearer scheme + composite auth policy +
`Result`'s new error-kind shape), but wire **no ownership check yet** — that's Build Day 30. Today's
goal, verbatim from the plan: "build clean, existing 20/20 tests still pass, new scheme provably
rejects requests with no/invalid JWT on the three routes (still 403/401 with no ownership logic
behind it)."

## Layout

```
ParkFlow/    - day-27/piece1's ParkFlow, copied here and extended (see below)
```

## 1. JWT bearer scheme, alongside the existing API key

[`Security/JwtBearerSetup.cs`](ParkFlow/src/ParkFlow.Api/Security/JwtBearerSetup.cs) adds
`Microsoft.AspNetCore.Authentication.JwtBearer`, validating a `sub` claim against a symmetric
signing key from `Security:Jwt:SigningKey`. `Program.cs` fails closed exactly like it already does
for `Security:ApiKey`: refuses to start outside `Development` if that key is missing.
[`Security/DemoUsers.cs`](ParkFlow/src/ParkFlow.Api/Security/DemoUsers.cs) fixes two known demo
user ids (`userA`/`userB`); a `Development`-only `POST /api/v1/dev/token` endpoint (`Program.cs`)
mints a token for one of them. Outside `Development` the endpoint isn't mapped at all — the
`DevTokenEndpoint_IsNotMappedOutsideDevelopment` test proves that with a valid API key the route is
a genuine 404, not a 401 that merely means "unauthenticated" (see note below on why that
distinction needed its own test).

One non-obvious fix along the way: `JwtSecurityTokenHandler.ValidateToken` tags the identity it
produces with `AuthenticationType = "AuthenticationTypes.Federation"`, not the scheme name
`"Bearer"`. The composite policy below needs to tell "authenticated via API key" apart from
"authenticated via bearer" by that name, so `JwtBearerEvents.OnTokenValidated` re-tags the identity
before it reaches authorization. Caught by a failing test, not by inspection — see "What I
learned."

## 2. The composite policy — and why the obvious attribute doesn't work

BUILD-PLAN.md's own wording for today was `[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]`.
Reading it against how ASP.NET Core actually evaluates multiple schemes: that attribute merges the
`ClaimsPrincipal` from every scheme that *authenticates*, and `RequireAuthenticatedUser()` only
checks that at least one succeeded — it's an **OR**, not an AND. ADR-001 explicitly calls this risk
out under "Negative / accepted risk": *"the composite policy needs its own test coverage so the
'and' isn't accidentally an 'or'."*

So [`Security/ReservationMutationPolicy.cs`](ParkFlow/src/ParkFlow.Api/Security/ReservationMutationPolicy.cs)
does it properly: a named policy with authentication schemes `ApiKey` + `Bearer`, plus a
`RequireApiKeyAndBearerRequirement`/`Handler` pair that only succeeds when *both* scheme identities
are present and authenticated. `ReservationsController.Cancel/CheckIn/Complete` use
`[Authorize(Policy = ReservationMutationPolicy.Name)]`; `Create` is untouched (API-key-only, per the
plan — it doesn't need an ownership check). The resulting matrix, all covered by
`ReservationMutationAuthTests.cs`:

| API key | Bearer | Result |
|---|---|---|
| ✗ | ✗ | 401 |
| ✓ | ✗ | 403 |
| ✗ | ✓ | 403 |
| ✓ | ✓ | passes through to the (unchanged) application service — 204/400 depending on business state |

## 3. `Result`'s error-kind shape

[`Result.cs`](ParkFlow/src/BuildingBlocks/ParkFlow.BuildingBlocks.Application/Result.cs) gains
`ResultErrorKind { Validation, NotFound, Forbidden }` and `NotFound(...)`/`Forbidden(...)` factory
methods, per the gap DESIGN-REVIEW.md surfaced (404 and 403 both collapsing into a bare `BadRequest`
today). `Failure(...)` defaults to `Validation` so every existing call site keeps compiling
unchanged — this piece only fixes the *shape*; nothing yet constructs a `Forbidden` result, and
`ReservationsController` still maps every failure to `BadRequest` exactly as before. Both are
Build Day 30's job, once there's an actual ownership check to report. `ResultTests.cs` (new, 8
tests) covers the shape only.

## Curl walkthrough (happy path against the real running API)

```
$ curl -X POST .../reservations/<id>/cancel                                        # no auth
401

$ curl -X POST .../reservations -H "X-Api-Key: $KEY" -d '{...}'                    # Create unchanged
{"reservationId":"eb234d5c-7262-4ef3-8e99-78a7250a644a"}

$ curl -X POST .../reservations/<id>/cancel -H "X-Api-Key: $KEY"                   # key only
403

$ curl -X POST .../dev/token -d '{"user":"userA"}'                                 # Development only
{"token":"eyJhbGciOi...","userId":"11111111-...","expiresAt":"2026-09-15T12:57:02Z"}

$ curl -X POST .../reservations/<id>/cancel -H "X-Api-Key: $KEY" -H "Authorization: Bearer $TOKEN"
204
```

Ran against `dotnet run` for real (not just the test suite) — full output in this piece's session
log. `dotnet build ParkFlow.slnx`: 0 warnings, 0 errors. `dotnet test ParkFlow.slnx`: **44/44
passing** (day-27's 20 + 8 new `ResultTests` + 16 new `ReservationMutationAuthTests`), 0
regressions.

## What I learned this session

Writing the composite policy the "obvious" way first — the exact attribute BUILD-PLAN.md wrote —
and then finding my own `MutationRoute_WithApiKeyOnly_Returns403` test fail with `NoContent`
instead of `Forbidden` is what actually surfaced the OR-vs-AND gap, not re-reading the ADR's warning
about it. The plan already named the risk in prose; I still needed a red test to believe it, and
then a second, subtler bug (the `AuthenticationTypes.Federation` mistagging) hid behind the *first*
fix long enough that the "valid key + valid token" case kept failing for a completely different
reason after the OR/AND bug was already resolved. Two independent bugs on the same feature, each
looking at first like the other one wasn't fully fixed — worth the extra half hour to isolate them
with a standalone mint-and-validate test before touching the HTTP layer at all.

## What would break this

| Failure | Status |
|---|---|
| `[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]` used as-is (the OR bug) | Avoided — `RequireApiKeyAndBearerHandler` checks both identities explicitly; `MutationRoute_WithApiKeyOnly_Returns403` / `WithBearerOnly_Returns403` prove one alone isn't enough. |
| The bearer identity mistagged as `"AuthenticationTypes.Federation"` instead of `"Bearer"` | Fixed — `JwtBearerEvents.OnTokenValidated` re-tags it; `Cancel_WithApiKeyAndBearer_PassesAuthAndReachesTheApplicationService` is the regression test that would catch this coming back. |
| The dev token endpoint reachable outside `Development` | Not mapped at all outside `Development` (by omission, not a runtime check) — `DevTokenEndpoint_IsNotMappedOutsideDevelopment` proves a genuine 404, not just an unauthenticated 401. |
| `Security:Jwt:SigningKey` missing outside `Development` | Fails closed — app refuses to start, mirroring the existing `Security:ApiKey` guard. Not covered by an automated test this piece (would need a process-level test, not just `WebApplicationFactory`); manually verified the guard clause fires. |
| A caller still claims to be a different user (the actual BOLA gap) | **Still open — by design, this piece.** Any valid demo user's token passes every check here; nothing yet compares it against `reservation.UserId`. That's Build Day 30, in full. |
| A 403 and a 404 both reported as one generic 400 | `Result`'s shape is fixed; the controller mapping is not — still Build Day 30. |

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/main/day-29/piece1

(Not pushed or committed — I don't commit or push without being asked, per standing preference.
Ready for you to review, stage, and push yourself; happy to draft a commit message on request.)

## Notes for mentor

- `cd day-29/piece1/ParkFlow && dotnet build ParkFlow.slnx` — 0 warnings, 0 errors, same 20
  projects as day-27/piece1 (no new projects; the identity plumbing lives in existing `ParkFlow.Api`
  and `ParkFlow.BuildingBlocks.Application`).
- `dotnet test ParkFlow.slnx` — 44/44 passing.
- The curl walkthrough above was run against a real `dotnet run` instance on
  `http://127.0.0.1:5299`, not just asserted through the test suite — happy to re-run it live.
- The two bugs under "What I learned" (OR-vs-AND, and the Federation mistagging) are exactly the
  kind of thing ADR-001's own "Negative / accepted risk" section predicted needing test coverage
  for — I'd flag this piece as evidence that the prediction was warranted, not just diligence
  theater.
- Deliberately did not touch `ReservationApplicationService.cs` or change what
  `ReservationsController` does with a `Result` — BUILD-PLAN.md says "no controller changes yet"
  for today, and the ownership check itself needs `Result.Forbidden` to have somewhere to go, which
  is Build Day 30's job.
