# Day 28 / Piece 1 — Design review + ADR on the ParkFlow capstone

Design-review day: no code changes to the capstone itself. This piece writes the ADR for the one
decision that matters most right now, plans the build days that would implement it, and records the
critique that shaped the final design. Built on top of
[day-22/piece2](../../day-22/piece2/ParkFlow) (the ParkFlow architecture) and
[day-27/piece1](../../day-27/piece1) (the security pass and threat model) — both left untouched;
the specific files this piece cites are copied, unmodified, into [`reference/`](reference/).

## Layout

```
ADR-001-reservation-ownership-authorization.md   - the ADR
BUILD-PLAN.md                                     - day-by-day plan to implement it
DESIGN-REVIEW.md                                  - the critique and how it changed the ADR
reference/                                        - unmodified copies of the files the ADR cites
```

## The one decision that matters most

[THREAT-MODEL.md §3.1](reference/THREAT-MODEL.md) (written day-27) ranks reservation-ownership —
the BOLA gap where `CancelAsync`/`CheckInAsync`/`CompleteAsync` never check that the caller owns the
reservation — as the #1 finding across the whole capstone, and the day-27 README flagged it as "the
one thing I'd flag hardest for review." Every other Day 27 finding is either fully fixed or a
straightforward follow-on; this one needed an actual architectural choice, which is what
[ADR-001](ADR-001-reservation-ownership-authorization.md) makes: a minimal per-user JWT bearer
scheme, added alongside (not instead of) the existing API-key scheme, plus an explicit ownership
guard in the three vulnerable methods — chosen over three alternatives (do nothing, trust a
spoofable header, or duplicate `day-25`'s full Entra ID setup onto a different app) for reasons laid
out in the ADR itself.

## The build plan

[BUILD-PLAN.md](BUILD-PLAN.md) — four build days (29–32): identity plumbing and a `Result`
error-kind fix first, then the ownership check and its tests, then a regression/ZAP pass and the
threat-model close-out, then (stretch) swapping the course's dev token issuer for a real Entra ID
app registration to prove the design's forward-compatibility claim.

## The critique

[DESIGN-REVIEW.md](DESIGN-REVIEW.md) — two critiques on the first draft. A mentor-level one caught
the first draft treating a spoofable `X-User-Id` header as an acceptable *interim* step; it was cut
entirely rather than staged, because a security control that only looks like a fix is worse than an
honestly-documented gap. A peer-level one asked why keep two auth schemes instead of one — the
decision didn't change, but the ADR now states explicitly why "which client" and "which user" are
different questions that happen to look redundant in a single-tenant setup. A third issue,
`Result.cs` collapsing 403 and 404 into the same `BadRequest`, surfaced during the same review pass
and was folded into Build Day 29 rather than left for implementation to discover.

## What I learned this session

Writing the ADR before writing any code is what caught the placebo option — on paper, "read a
header, trust it" reads like a reasonable staging step toward the real fix, and it was only when a
reviewer asked "what stops an attacker from setting that header themselves" that it became obvious
the option should never have been in the "staged rollout" column at all. A design review's value
isn't just picking the right final answer, it's catching the wrong answer that would otherwise have
shipped disguised as a smaller, safer first step.

## What would break this

| Failure | Status |
|---|---|
| A caller claims to be a different user by setting a trusted-but-unverified header | Never implemented — rejected in review before it reached code (see DESIGN-REVIEW.md). |
| The `Development`-only token endpoint reachable in a real deployment | Planned to fail closed the same way `ApiKeyAuthenticationHandler` already does outside `Development` — not yet built, so unverified until Build Day 29 lands. |
| A 403 (not yours) and a 404 (doesn't exist) both reported as one generic 400 | Caught in review, not yet fixed — first task in Build Day 29 (`Result` error-kind). |
| The JWT scheme added without the existing API key, silently dropping "which client is calling" | Avoided by keeping both schemes — see ADR-001's Decision section for why they answer different questions. |
| This plan never gets implemented (stays a design doc forever) | The real risk of any design-review day — Build Day 29 is written concretely enough (file names, method signatures) that it shouldn't need re-deriving from scratch when picked up. |

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/main/day-28/piece1

(Not yet pushed — I don't commit or push without being asked, per standing preference. Ready for
you to review, stage, and push yourself.)

## Notes for mentor

- No `dotnet build`/`dotnet test` for this piece — it's documentation, not code; `day-22/piece2` and
  `day-27/piece1` are unchanged and still build/test exactly as their own READMEs describe.
- `reference/` holds unmodified copies of the five files this ADR cites directly
  (`ReservationApplicationService.cs`, `ReservationsController.cs`,
  `ApiKeyAuthenticationHandler.cs`, `Result.cs`, `THREAT-MODEL.md`) so this piece is self-contained
  without touching day-22 or day-27.
- The critique in DESIGN-REVIEW.md is written the same way day-27's README flagged its own BOLA gap
  for discussion — I'd rather hand you a design with its own weak first draft shown and corrected
  than one that looks like it was right the first time.
