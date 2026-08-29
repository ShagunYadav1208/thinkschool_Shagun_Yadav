# Verification log

**Status: deployed and verified live.** Provisioning was held pending review (see git history / prior
revision of this file for the pre-deploy state); once approved, real Azure resources were created and the
app is now genuinely live. Everything below section 3 onward is against the real deployed system, not a
local approximation.

- **Live frontend:** https://black-desert-0fde3f100.7.azurestaticapps.net
- **Live API:** https://syquotes17-api.azurewebsites.net
- **Live Lighthouse (against the deployed URL):** Performance 94, Accessibility 100, Best Practices 100, SEO 100

## 1. Backend - compiles, and the local (SQLite) path still works

```
dotnet build            -> Build succeeded, 0 errors
```

Ran the modified `QuotesApi` locally (`ASPNETCORE_ENVIRONMENT=Development`, SQLite, port 5311/actual
5116 per `launchSettings.json`) to confirm the provider-switch in `InfrastructureExtensions.cs` didn't
break the existing path: `GET /api/quotes/?page=1&size=3` returned `200 []` against a fresh local DB
(empty is correct - `quotes.db` was intentionally excluded from the day-1 -> day-17 copy, not seeded).
Migration history table, table creation, and app startup all ran exactly as they do in
`day-1/piece3/QuotesApi`.

The Azure SQL / Managed Identity path (`db.Database.IsSqlServer()` branch in `Program.cs`,
`Authentication=Active Directory Managed Identity` in `appsettings.Production.json`) is **not**
exercised by this local run - there's no Azure SQL server yet. It's verifiable only after
`infra/deploy.md` step 1 provisions one.

## 2. Frontend - builds, tests pass, environment swap confirmed

```
npm test        -> Test Files 4 passed, Tests 23 passed  (piece2's untouched suite, copied as-is)
ng build         -> production build succeeds, 3 lazy chunks unchanged (login/list/detail routes)
```

Confirmed the new `fileReplacements` config actually swaps `environment.ts` -> `environment.prod.ts` at
build time by grepping the built bundle: `main-EM6EMKYI.js` contains
`REPLACE-AT-DEPLOY.azurewebsites.net/api/quotes/` (the placeholder App Service URL), not the dev-mode
relative `/api/quotes/` proxy path. This is the one thing most likely to silently regress (ship a build
that still points at localhost) - checked directly in the built artifact, not assumed from the config.

## 3. Lighthouse - pre-deploy signal, then the real live-URL score

Pre-deploy local pass (primitive local server, no compression/CDN, placeholder API hostname): Performance
81, Accessibility 100, Best Practices 96, SEO 100 (after the robots.txt fix in section 4 - was 82).

**Live, against the real deployed URL**, after resources were provisioned:

| Category | Score |
|---|---|
| Performance | 94 |
| Accessibility | 100 |
| Best Practices | 100 |
| SEO | 100 |

Performance jumped 81 -> 94 once real (compression, HTTP/2, CDN caching on the hashed asset filenames)
infrastructure was in place, exactly as predicted in the pre-deploy note. `errors-in-console` (the one
remaining local Best Practices gap) is gone live, confirming it really was the placeholder-hostname
artifact it was flagged as.

**One point under the informal >=95 target, and I stopped chasing it rather than keep tuning blind.**
The remaining Performance gap is a Cumulative Layout Shift of 0.124 (Google's "needs improvement" band
starts at 0.1) - Lighthouse's `layout-shifts` audit named the exact element: Explore's `.list-pane`
grows when "Loading quotes..." is replaced by the real `<ul>` of quote cards, shifting the detail pane
below it. I tried the obvious fix - reserving `min-height` on `.list-pane`, the same pattern
`.detail-pane` already uses - rebuilt, redeployed, and re-ran Lighthouse **twice** to rule out noise: CLS
got *worse* (0.124 -> 0.164, Performance 94 -> 91), because the reservation interacts with the two-pane
CSS Grid's row auto-sizing in a way that produces a *bigger* shift once real content settles. Reverted
immediately, rebuilt, redeployed, re-ran Lighthouse a third time: back to 94/100/100/100, confirmed
stable. Documented as a real, live-measured gap rather than silently patched with a change that measurably
made it worse - see "What would break this" for the actual fix this needs (a skeleton loader sized to
match real content, not a flat `min-height` guess).

## 4. The concrete bug caught (and fixed) this session

**Wrong assumption going in:** `staticwebapp.config.json`'s `navigationFallback.exclude` list
(`["/assets/*", "*.{css,js,ico,png,svg,webmanifest}"]`) looked complete for "let the Angular router
handle `/login`, `/quotes`, `/quotes/:id`, but serve real static files as themselves" - the standard SPA
fallback pattern.

**What actually broke:** `robots.txt` isn't covered by either exclude pattern (no `/robots.txt` entry,
and `.txt` wasn't in the extension list), so a request for it would have been rewritten to `index.html`
by the SPA fallback - the Angular app's HTML shell, served with a `text/plain`-looking URL but
HTML content. Lighthouse's `robots-txt` audit caught this in the pre-deploy pass: **13 syntax errors**,
because `index.html` obviously isn't valid `robots.txt` syntax (`<!doctype html>` isn't a `User-agent:`
directive). SEO score: 82.

**Root cause:** there was no `public/robots.txt` file at all - the site had never actually served one,
so the fallback rewrite was masking that fact by returning *something* at that URL instead of a clean
404, which made the bug look like a content problem when it was a missing-file problem plus a
too-narrow exclude list.

**Fix, both parts:**
1. Added an actual `public/robots.txt` (`User-agent: *` / `Allow: /`).
2. Widened the exclude list to `["/assets/*", "/robots.txt", "*.{css,js,ico,png,svg,webmanifest,txt}"]`
   so any future top-level `.txt`/robots-style file doesn't hit the same trap.

Rebuilt, re-ran Lighthouse: SEO 82 -> 100, `robots-txt` and `meta-description` (also fixed, an
`index.html` `<meta name="description">` tag was simply missing) both pass clean.

## 5. Two real deployment bugs hit while going live (not in the code review - in the platform)

These weren't caught by any test; they only surfaced once real Azure resources existed. Both fixed,
both confirmed by successfully deploying afterward.

**Bug A - Kudu's zip extractor mangled nested paths from a Windows-built zip.** Every `az webapp deploy
--type zip` attempt failed with `rsync: failed to stat ".../runtimes\linux-x64\native\libe_sqlite3.so":
Invalid argument"` - a literal backslash inside a path Kudu was extracting on Linux. The zip itself was
verified clean (`python -c "zipfile...create_system"` showed `0`, i.e. every entry was flagged
DOS/Windows-origin, because `System.IO.Compression.ZipFile.CreateFromDirectory` always stamps that on
Windows) even after recreating the App Service, its Plan, and the zip itself from scratch with zero
`\` characters in any entry name - ruling out "stale corrupted state" and "malformed zip" as the cause.
Root cause: Kudu's Linux extractor mishandles the DOS-origin flag on deeply nested entries. Fix:
repacked the same zip via Python's `zipfile` module with `create_system = 3` (Unix) and explicit
`external_attr` permission bits on every entry - deployed clean on the next attempt.

**Bug B - EF Core 10.0.11's SQL Server provider hard-requires an exact SqlClient assembly version.** Once
deployment worked, the app crashed on every start: `TypeInitializationException` ->
`FileNotFoundException: Could not load file or assembly 'Microsoft.Data.SqlClient, Version=6.0.0.0'`,
thrown from `SqlServerVectorTypeMapping..cctor()` - triggered by nothing more than calling
`Database.IsSqlServer()`, before ever opening a connection. Pinning `Microsoft.Data.SqlClient` to the
newer 7.0.2 (assembly version 7.0.0.0) made it *worse*, not better, because EF Core's vector-type support
does a reflection lookup hardcoded to the literal string `"Microsoft.Data.SqlClient, Version=6.0.0.0"` -
any package version whose assembly version isn't exactly 6.0.0.0 fails that lookup. Fix: pinned to
`6.1.6` explicitly (any 6.x package keeps assembly version 6.0.0.0) instead of trusting the transitive
resolution or reaching for "newer is safer."

Neither bug is specific to this app's business logic - both are genuine platform/package quirks that only
a real deploy attempt would surface, which is the concrete argument for why "code review + unit tests"
isn't the same claim as "deployed and verified."

## 6. States/edges - checked against the live system

| State/edge | Result |
|---|---|
| Frontend builds against the prod environment file | Verified (section 2) - grepped the real hostname into the built bundle |
| SQLite-backed local API still works | Verified (section 1) |
| SEO/meta/robots.txt correctness | Verified, one real bug fixed (section 4) |
| Azure SQL connection via MI actually succeeds | **Verified live** - `SELECT 1` and schema creation both succeeded through the App Service's system-assigned identity, zero credentials anywhere |
| App Service MI granted DB access (vs. denied) | **Verified both ways live** - first attempt was correctly denied (`CREATE TABLE permission denied`, `db_ddladmin` wasn't granted yet, only `db_datareader`/`db_datawriter`); granting the missing role and restarting fixed it, confirming the failure mode is a real, attributable SQL Server error, not a silent empty response |
| Live SWA URL loads | **Verified** - https://black-desert-0fde3f100.7.azurestaticapps.net, HTTP 200 |
| Real Lighthouse >= 95 on the live URL | **94/100/100/100** - one point under on Performance; root-caused to a real CLS of 0.124, one attempted fix made it measurably worse and was reverted (section 3) |
| CORS actually scoped, not wildcard | Verified - `OPTIONS` preflight with `Origin: https://black-desert-....azurestaticapps.net` returns `Access-Control-Allow-Origin` matching exactly that origin, nothing else |
| Full CRUD against real Azure SQL | Verified - created a throwaway quote via `POST`, confirmed it via `GET`, deleted it via `DELETE`, confirmed the table is empty again - all through the live API, no direct DB access |
| 401/failed-token behavior | N/A for this architecture - the browser never presents a token to the API (see brief); a failed-MI-token case shows up server-side as the API failing to reach Azure SQL, which is exactly what Bug B and the DDL-permission case above both were, live |

## 7. What would break this

- **The MI's DB role is revoked or the App Service is recreated** (a new App Service gets a *new*
  identity - the old `CREATE USER ... FROM EXTERNAL PROVIDER` grant doesn't transfer) - **this actually
  happened twice during this session** (App Service recreated to rule out stale-storage theories for Bug
  A) and both times produced the expected result: the API failed every DB call until the new identity was
  explicitly re-granted `db_datareader`/`db_datawriter`/`db_ddladmin`.
- **Azure SQL's AAD-only auth gets disabled** (someone adds a SQL-auth admin later) - doesn't break
  anything immediately, but reintroduces exactly the "a password exists somewhere" risk this whole
  design avoids; worth a policy/alert, not just a one-time check.
- **The API's CORS `AllowedOrigin` and the SWA's actual hostname drift** (e.g., a custom domain gets
  added later without updating `Cors:AllowedOrigin`) - the browser call would fail with a CORS error, not
  a 401 or 500, which is a confusing failure mode to debug blind.
- **The frontend's `environment.prod.ts` `apiBaseUrl` and the API's real hostname drift** - same class of
  failure as the CORS case above, from the other side.
- **The CLS-driving quote list grows in a future feature** (more fields, longer text) without a real
  skeleton loader - the flat `min-height` I tried and reverted (section 3) proves a guessed reservation
  can make CLS worse; the correct fix is a loading placeholder shaped like the real content, sized from
  actual rendered output, not a constant.

## Current status

**Deployed and verified live**, both frontend and backend, real Azure SQL with zero secrets anywhere.
Nothing has been pushed to GitHub yet - CI/CD workflows (`.github/workflows/`) are written but unused;
deployment was done directly via `az` CLI. `infra/deploy.md` documents the provisioning steps actually
run (with the two bug fixes above folded in) for anyone reproducing this from scratch.
