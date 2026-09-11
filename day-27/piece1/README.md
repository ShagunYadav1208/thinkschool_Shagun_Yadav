# Day 27 / Piece 1 — Security pass on the ParkFlow capstone

Built on top of [day-22/piece2](../../day-22/piece2/ParkFlow) — the ParkFlow modular-monolith
capstone — not on the separate `QuotesApi` track that days 23–26 continued. `ParkFlow/` here is
day-22/piece2's ParkFlow, copied in and then hardened per this piece's brief; day-22/piece2 itself
is untouched. See "Which capstone" below for why that distinction mattered this time.

## Layout

```
THREAT-MODEL.md        - STRIDE-lite threat model for the ParkFlow capstone
SECURITY-TESTING.md     - the OWASP ZAP baseline run: setup, findings, fix, before/after
ParkFlow/                - day-22/piece2's ParkFlow, copied here and hardened (see below)
infra/                   - new: the target Azure architecture, data tier behind a private endpoint
zap/                     - ZAP scan artifacts: hook.py, reports, run log
```

## Which capstone

This student's course has two separate multi-day tracks running in parallel: the **ParkFlow**
capstone (started day-22/piece2, a from-scratch modular monolith) and a **QuotesApi** exercise
that days 15 through 26 built up (background jobs, caching, Service Bus, Entra ID auth, App
Insights — see the `project-day25-entra-id-feature-flagged` and
`project-day26-appinsights-undeployed` memories). Day 27's brief asks for a security pass on "the
capstone," and the capstone is ParkFlow, not QuotesApi — this piece is built on day-22/piece2
accordingly. The Entra ID work from day-25 is real but belongs to the other track; THREAT-MODEL.md
section 5 explains why this piece uses a different (simpler) auth mechanism for ParkFlow rather
than duplicating that setup here.

## 1. Threat model

[THREAT-MODEL.md](THREAT-MODEL.md) — STRIDE-lite across ParkFlow's three trust boundaries (client
↔ API, API ↔ data tier, outbox → broker). Ranked findings, and what got fixed this pass vs. what
didn't (the reservation-ownership/BOLA gap is the most important thing this document says **not**
fixed — see section 3.1 and section 4).

## 2. Private endpoint for the data tier

[infra/](infra/) — new Bicep for ParkFlow (day-22/piece2 had none; it only ever ran on EF Core
InMemory). `publicNetworkAccess: 'Disabled'` on the SQL logical server, reachable only through a
private endpoint in a VNet the App Service is regionally integrated into. `az bicep build` compiles
clean, 0 warnings. Never deployed — blocked by the same disabled Azure for Students subscription
recorded in the `project-azure-subscription-disabled` memory; see `infra/README.md` for the full
"why undeployed" and what's specifically not built (a Key Vault for the API key, the Managed
Identity → SQL grant).

## 3. OpenAPI surface hardening

All in [`ParkFlow/src/ParkFlow.Api`](ParkFlow/src/ParkFlow.Api):

- **Auth**: every endpoint requires an `X-Api-Key` header (`Security/ApiKeyAuthenticationHandler.cs`),
  enforced via an authorization fallback policy, except `/health`. Fails closed: the app refuses to
  start outside `Development` if no key is configured. See THREAT-MODEL.md section 5 for why an
  API key and not Entra ID this pass.
- **Versioning**: `Asp.Versioning` — every route is now `api/v1/...` (was unversioned).
- **Input limits**: `CreateReservationRequest` and `RegisterVehicleRequest` went from zero
  validation to `[Required]`/`[Range]`/`[StringLength]`/a custom `NotEmptyGuid` attribute, plus
  `IValidatableObject` cross-field checks (end-after-start, not-in-the-past, max 30-day span on a
  reservation). Kestrel's `MaxRequestBodySize` is capped at 32KB, `[RequestSizeLimit(4096)]` on the
  two POST actions. A fixed-window rate limiter (60 req/min per API key or IP) covers every route.
- **OpenAPI document itself**: `Security/ApiKeySecuritySchemeTransformer.cs` adds the `ApiKey`
  security scheme to every operation in the generated `/openapi/v1.json` — the contract now tells
  the truth about what it takes to call this API, not just the runtime.
- **Error handling**: a centralized exception handler maps domain guard-clause exceptions
  (`ArgumentException`/`InvalidOperationException`) to `400`, everything else to a bare `500` with
  no stack trace, ever, regardless of environment.
- **Security headers**: `X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy`,
  `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Resource-Policy` on every response; no
  `Server` header.

New tests: `tests/ParkFlow.IntegrationTests/SecurityHardeningTests.cs` — 9 tests proving the above
actually works at runtime (401 without/with-wrong key, 400 on each invalid-input case, 201 on
valid, headers present). Full suite: **20/20 passing** (`dotnet test ParkFlow.slnx` — the original
11 from day-22/piece2 plus these 9), `dotnet build` 0 warnings / 0 errors.

## 4. Basic pen test

[SECURITY-TESTING.md](SECURITY-TESTING.md) — a real OWASP ZAP baseline scan (Docker,
`ghcr.io/zaproxy/zaproxy:stable`) against the hardened API running in a production-like posture.
One real finding on the first run (missing `Cross-Origin-Resource-Policy` header), fixed, rescanned
clean: **66/66 passive rules pass, 0 warnings, 0 failures, across 22 endpoints** (up from the 4 a
plain spider would have found — see that document for how the scan was made to actually reach
every authenticated route via an OpenAPI import).

## What I learned this session

Two things. First, that a scanner is only as useful as what you feed it — a default ZAP baseline
against an authenticated JSON API finds almost nothing by default (4 URLs, mostly noise); making it
actually exercise the API meant teaching it the auth (a replacer rule) and the shape of every
endpoint (importing the OpenAPI document ZAP's own hardening work now produces). Second, and more
important: writing the threat model *before* touching code is what surfaced the BOLA gap in the
Reservation actions — that finding didn't come from the ZAP scan (a passive scanner can't see
"this GUID belongs to a different user"), it came from just reading `CancelAsync`/`CheckInAsync`/
`CompleteAsync` with an attacker's eyes. Threat modeling and pen testing catch genuinely different
classes of bug; doing only one of them would have missed the other's finding entirely.

## What would break this

| Failure | Status |
|---|---|
| Any endpoint called without/with a wrong API key | Fixed this pass — 401, proven by test and by the ZAP scan's own 87%-4xx stat. |
| A reservation cancelled/checked-in/completed by someone other than its owner | **Not fixed.** THREAT-MODEL.md section 3.1 — the API key authenticates a client of the API, not a specific user, so it can't close this by itself. Needs real per-user identity plus an ownership check in `ReservationApplicationService`. |
| A malformed or oversized request body | Fixed — model validation (400) or Kestrel's body-size limit (413/connection reset) before it ever reaches the domain layer. |
| One client flooding the API | Fixed — 60 req/min fixed-window rate limit, 429 past that. |
| The SQL data tier reachable directly, bypassing the API | Addressed at the infra layer (`publicNetworkAccess: 'Disabled'` + private endpoint) but **undeployed** — see infra/README.md. |
| An unhandled exception leaking internals | Fixed — centralized handler, no stack trace ever reaches a client. |

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/main/day-27/piece1

(Not yet pushed — I don't commit or push without being asked, per standing preference. Ready for
you to review, stage, and push yourself.)

## Notes for mentor

- `cd day-27/piece1/ParkFlow && dotnet build ParkFlow.slnx` — 0 warnings, 0 errors, 22 projects
  (day-22/piece2's 20 plus no new projects — the hardening lives in existing projects, not new
  ones).
- `dotnet test ParkFlow.slnx` — 20/20 passing.
- The ZAP scan and the private-endpoint Bicep are both real work product (a genuine Docker-run
  scan; a genuine `az bicep build`-clean template), but the Bicep was never deployed — see
  `infra/README.md` for exactly why (the same disabled Azure for Students subscription already
  recorded against Day 25/26).
- The one thing I'd flag hardest for review: THREAT-MODEL.md section 3.1's BOLA finding. I chose
  to document it clearly rather than paper over it with the API-key auth this pass adds, since that
  auth genuinely doesn't fix it — happy to discuss whether that call (fix now vs. document now,
  fix later) was the right one for this piece's scope.
