# OWASP ZAP baseline — ParkFlow (Day 27)

Real scan, not simulated: `ghcr.io/zaproxy/zaproxy:stable`'s `zap-baseline.py` run via Docker
against the hardened `ParkFlow.Api` running locally in a production-like posture
(`ASPNETCORE_ENVIRONMENT=Production`, a real `Security:ApiKey` set via environment variable, no
debugger attached). Artifacts in this folder: `zap-baseline-report.{html,md,json}`,
`zap-scan-run.log` (the final clean run's console output), `hook.py` (see below).

## What "baseline" means here

`zap-baseline.py` is ZAP's spider + **passive** scan — it crawls what it can reach and inspects
real traffic for issues (missing headers, information disclosure, cookie flags, etc.); it does not
send attack payloads (that's the separate "full scan"/active scan). "Basic pen test" in this
piece's brief is this baseline pass, which is the appropriate first step before anything more
aggressive against a service with no staging environment.

## Getting a meaningful scan out of a JSON API with auth

A default baseline run against this API is nearly useless: every route needs `X-Api-Key`, and a
JSON API has no `<a href>` links for the spider to follow. Two things fixed that, both in
`hook.py` (a `zap_started` hook baseline.py loads via `--hook`):

1. **`zap.replacer.add_rule(...)`** adds `X-Api-Key: <test key>` to every outgoing request, so the
   scan sees the API in its real, authenticated posture instead of only ever hitting 401s.
2. **`zap.openapi.import_url(...)`** imports `/openapi/v1.json` (see THREAT-MODEL.md section 5 and
   `Program.cs` — the document itself now declares the `ApiKey` security scheme) into ZAP's site
   tree, seeding every defined operation. This is what took the scan from **4 URLs** (root,
   `/health`, `robots.txt`, `sitemap.xml` — everything a plain spider found) to **22 URLs** — every
   real endpoint across all three controllers, at every declared route.

The API key used for the scan was a random, single-use value generated for this session
(`Security__ApiKey` set as a process environment variable, never written to a file that was kept)
— not a real credential, and not the `appsettings.Development.json` placeholder either.

## Run 1 — before the last fix

```
Total of 22 URLs
...
WARN-NEW: Cross-Origin-Resource-Policy Header Missing or Invalid [90004] x 2
FAIL-NEW: 0  FAIL-INPROG: 0  WARN-NEW: 1  WARN-INPROG: 0  INFO: 0  IGNORE: 0  PASS: 65
```

One real finding: no `Cross-Origin-Resource-Policy` header, flagged on 2 of the scanned responses
(ZAP's Spectre-related isolation check, rule 90004). Everything else — the full set of passive
rules ZAP ships (66 in total; see the "PASS:" list in `zap-scan-run.log` for the complete set,
covering clickjacking, MIME sniffing, CSP, cookie flags, information disclosure, cache headers,
and more) — passed on the first run, because the Day 27 hardening (`SecurityHeadersMiddleware`,
`AddServerHeader = false`, the centralized exception handler) was already in place before this scan
started, not retrofitted afterward.

## Fix applied

Added `Cross-Origin-Resource-Policy: same-origin` to
[`ParkFlow.Api/Security/SecurityHeadersMiddleware.cs`](ParkFlow/src/ParkFlow.Api/Security/SecurityHeadersMiddleware.cs).
`same-origin` (the strictest value) is correct today because no browser-based client of this API
exists yet (see THREAT-MODEL.md) — the day a legitimate cross-origin consumer shows up, relaxing
this should be a deliberate, reviewed change, not a default left wide open pre-emptively. Covered
by `SecurityHardeningTests.ResponsesInclude_SecurityHeaders`.

## Run 2 — after the fix (final)

```
Total of 22 URLs
...
FAIL-NEW: 0  FAIL-INPROG: 0  WARN-NEW: 0  WARN-INPROG: 0  INFO: 0  IGNORE: 0  PASS: 66
```

Clean: 66/66 passive rules pass, 0 warnings, 0 failures, across all 22 discovered endpoints
(`zap-baseline-report.md`'s "Summary of Alerts" table: 0 High, 0 Medium, 0 Low, 2 Informational —
both informational findings are "Non-Storable Content" on the two `POST` write endpoints, which is
correct and expected: reservation/vehicle-creation responses are not supposed to be cacheable).

## What this run does and doesn't prove

- **Does prove:** every scanned endpoint requires the API key (87% of responses were 4xx in the
  scan's own insight stats — expected, since ZAP also probes each URL with other HTTP methods and
  malformed calls that correctly get rejected); no unhandled exception leaked a stack trace to any
  request in the run; the security headers are present on every response, not just `/health`.
- **Doesn't prove:** this is a passive scan. It didn't attempt SQL injection, and this API's
  in-memory EF Core provider for this piece means SQL injection isn't even a meaningful vector yet
  (see the Day 22 README) — this becomes relevant once a real SQL provider is wired in per
  `infra/`. It also can't and didn't test the BOLA finding in THREAT-MODEL.md section 3.1 — that
  needs a logic-aware test (two different callers, one reservation), not a generic scanner, and is
  explicitly called out there as not fixed this pass.
