# Postmortem — ParkFlow / ADR-001 (Days 27–32)

## What shipped

The reservation-ownership fix (ADR-001, day-28/piece1) is live: a JWT bearer scheme alongside the
existing API key, a composite policy requiring both on the three mutation routes, and a real
ownership check in `ReservationApplicationService` — closing the #1 finding from day-27's threat
model. Built up over five days (identity plumbing → ownership enforcement → PR review → tests/perf/
security polish → this deployment), each day a real, tested, working state, not a checkpoint on the
way to one.

## What I'd do differently

I'd have stood up the deployment path on day 27, next to the Bicep that was written but never
applied. The app was deployment-ready well before today — every day since has run real tests
against a real (if local) instance — but "can this actually run somewhere reachable" stayed
untested until the very last day, which is exactly the wrong day to discover it. It didn't, in the
end: the only surprises were an Azure policy restricting which regions this subscription can deploy
into, and picking `DOTNETCORE:10.0` as the right Linux runtime moniker — both five-minute fixes, but
five minutes I only had because nothing else was left to build. Had the app not started cleanly on
App Service, this would've been a bad day to find out why.

## The hardest bug

Day 29's composite authorization policy passed its own tests, then failed a test I added five
minutes later for a completely different reason. The bug: `JwtSecurityTokenHandler.ValidateToken`
tags the identity it returns with `AuthenticationType = "AuthenticationTypes.Federation"` — not the
scheme name `"Bearer"` — so my handler's check for "did the Bearer scheme authenticate" silently
never matched, even with a perfectly valid token. It looked identical to the *first* bug I'd just
fixed (the `AuthenticationSchemes` attribute being an OR, not an AND), which made it genuinely
confusing: the same symptom (still 403 with both credentials presented), two unrelated causes,
stacked. What broke the deadlock was stepping outside the HTTP pipeline entirely — a plain
mint-a-token-and-call-`ValidateToken` unit test, no ASP.NET Core in the loop — which is what finally
showed a valid principal with the wrong `AuthenticationType`, not an invalid one.

## What I'm proudest of

Catching that BUILD-PLAN.md's own literal instruction —
`[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]` — doesn't do what ADR-001 needed. ASP.NET
Core merges the identity from every scheme that authenticates and only requires *one* to succeed;
written as specified, the composite policy would have been an OR, silently reopening the exact BOLA
gap this whole four-day arc existed to close, while looking like it was fixed. ADR-001 itself
flagged this as a risk needing test coverage — the flag was right, and a failing test is what
actually cashed it in, not a second reading of the plan.

## What would break this

| Failure | Status |
|---|---|
| The live demo instance runs `ASPNETCORE_ENVIRONMENT=Development` so `/dev/token` works | Deliberate, documented tradeoff for this demo only — see README.md. Never the posture for a real deployment; day-27's threat model already covers why the token endpoint fails closed outside `Development`. |
| Azure for Students' free tier (F1) — cold starts, no SLA, no autoscale | Fine for a demo, not for real traffic. A real launch needs at minimum a paid tier and the private-networking Bicep from day-27/piece1/infra (written, never applied). |
| The demo's JWT signing key and API key are freshly generated for this deployment only | Correct choice — never reuse the `dev-local-only-*` placeholders publicly. Neither is written to any file in this repo. |
| Someone mints a token for `userA`/`userB` and messes with the demo's own reservations | Low stakes (synthetic demo data, in-memory, resets on any redeploy/restart) but real — this is the accepted cost of a public `/dev/token` endpoint, not a defended-against threat. |

## What I learned this session

Shipping didn't teach me anything about the code — it taught me something about the plan. Every
prior day treated "does it work" as answerable locally, and it was, right up until "ship it live"
turned out to be its own small project (region policy, runtime monikers, secret generation) that
had been quietly deferred five times. The code was never the risk; the assumption that deployment
would be trivial because the code was solid, was.
