# Day 17 / Piece 1 — Deploy to Azure Static Web Apps

Frontend is [day-16/piece2](../../day-16/piece2)'s Angular app (routing/guards + signals-based
`QuoteManagementStore`), copied unmodified into [quotes-list-detail/](quotes-list-detail/). Backend is
[day-1/piece3](../../day-1/piece3)'s `QuotesApi`, copied into [QuotesApi/](QuotesApi/) so it could be
modified to run against Azure SQL via Managed Identity without touching the read-only original.

## Current status — read this first

**Deployed and verified live.**

- **Frontend:** https://black-desert-0fde3f100.7.azurestaticapps.net
- **API:** https://syquotes17-api.azurewebsites.net
- **Live Lighthouse:** Performance 94, Accessibility 100, Best Practices 100, SEO 100
- **Managed Identity to Azure SQL:** confirmed with a real write/read/delete round trip through the live
  API - zero secrets anywhere, see [verification-log.md](verification-log.md) sections 5-6.

Not yet pushed to GitHub - the CI/CD workflows in `.github/workflows/` are written but unused; the actual
deploy was done directly via `az` CLI (provisioning, code deploy, and two real platform bugs fixed along
the way - see verification-log.md section 5).

## 1. The brief

Full text in [brief-to-agent.md](brief-to-agent.md). The part it hinges on:

> Managed Identity is an Azure-to-Azure trust mechanism; it doesn't exist for a browser. So `QuotesApi`
> moves to Azure SQL + an App Service with a system-assigned identity - the connection string is
> `Authentication=Active Directory Managed Identity`, no username or password anywhere - and Azure SQL
> itself is Azure-AD-only, so there's no SQL-auth admin login to leak in the first place. The
> browser-to-API hop is still plain HTTPS + CORS, same as every prior day; MI secures the API-to-database
> hop, which is the part of "Managed Identity, no client secret" that's easy to state wrong.

No custom domain - the default `*.azurestaticapps.net` hostname, since a real custom domain isn't
something I own to point at this honestly.

## 2. The output

- **[QuotesApi/](QuotesApi/)** - `InfrastructureExtensions.cs` picks SQLite or SQL Server based on
  whether the connection string contains `Authentication=Active Directory` (no environment-variable
  branching to keep in sync); `Program.cs` uses `EnsureCreated` instead of the SQLite migration history
  for the Azure SQL path (documented simplification - see verification-log.md section 6); CORS added,
  scoped to the SWA's origin via config, not `AllowAnyOrigin`.
- **[quotes-list-detail/](quotes-list-detail/)** - `environment.prod.ts` (new) + an `angular.json`
  `fileReplacements` entry swap in the real API URL for production builds; `staticwebapp.config.json`
  handles SPA routing fallback (needed - piece1/piece2's router already relies on direct deep links like
  `/quotes/17` working on reload) plus security headers; `index.html` gained a meta description;
  `public/robots.txt` is new (see the bug below).
- **[infra/](infra/)** - `main.bicep` (+ `modules/sql.bicep`, `api.bicep`, `swa.bicep`), validated with
  `az bicep build` (compiles clean, not deployed). `deploy.md` is the full runbook: provision, fill in
  the two `REPLACE-AT-DEPLOY` placeholders, grant the MI database access (the one step Bicep can't
  express - a T-SQL `CREATE USER ... FROM EXTERNAL PROVIDER`), wire GitHub secrets, deploy, verify.
- **[.github/workflows/](.github/workflows/)** - frontend deploy via a scoped SWA deployment token;
  backend deploy via OIDC federated credentials (`azure/login`, no publish profile, no client secret -
  the trust is a federated-credential subject match on this repo+branch).

## 3. Verification log

Full detail in **[verification-log.md](verification-log.md)**:

- Backend builds clean; the local SQLite path re-verified working after the provider-switch change.
- Frontend: 23/23 tests pass (piece2's suite, untouched), production build confirmed to actually bundle
  the prod API URL (grepped the built JS, not assumed from config).
- **A real bug caught and fixed:** the SPA `navigationFallback` exclude list didn't cover
  `/robots.txt`, so a request for it was silently rewritten to `index.html` - Lighthouse's `robots-txt`
  audit caught this as 13 syntax errors (HTML isn't valid robots.txt), which is what surfaced a file that
  had never actually existed. Added a real `robots.txt`, widened the exclude list, SEO score went
  82 -> 100.
- **Two more real bugs, only found by actually deploying:** Kudu's Linux zip extractor mangled nested
  paths from a Windows-built zip (fixed by repacking with Unix-flagged entries), and EF Core 10.0.11's
  SQL Server provider crashes unless `Microsoft.Data.SqlClient` is pinned to exactly assembly version
  6.0.0.0 (fixed by pinning `6.1.6`, not the newer `7.0.2` I tried first, which made it worse). Full
  writeup in verification-log.md section 5.
- **Live, not simulated:** created a real quote through the live API, confirmed it via `GET`, deleted it
  via `DELETE` - a full CRUD round trip against real Azure SQL via the App Service's managed identity,
  and confirmed CORS is scoped to exactly the SWA's origin, not wildcarded.

## What did I learn this session?

"Managed Identity" as stated in the brief (frontend calls the API via MI) doesn't map onto how MI
actually works - it's an Azure-resource-to-Azure-resource mechanism, and a browser isn't an Azure
resource. Getting the architecture right meant relocating where MI actually applies (API-to-database)
rather than forcing the literal reading onto a browser call it can't do. Writing that reasoning into the
brief itself, before any code, is what kept the rest of the design honest instead of quietly faking a
"token" the frontend would attach to nothing.

## What would break this

See [verification-log.md](verification-log.md) section 6: the MI's DB role grant not transferring to a
recreated App Service, AAD-only auth getting disabled later, and the CORS-origin / API-hostname pairs
drifting out of sync on either side.

## GitHub link

Not pushed yet. Remote for the `thinkbridge-thinkschool` org is already configured locally as
`thinkschool` (`https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav.git`); link to follow
once pushed.

## Notes for mentor

- `day-1/piece3/QuotesApi`, `day-16/piece1`, and `day-16/piece2` were read-only reference / copy source;
  nothing there was modified.
- Deployed directly via `az` CLI rather than the GitHub Actions workflows (which are written but unused) -
  this was a working session with real iteration, including two genuine platform bugs (not application
  bugs) that only surfaced once real Azure resources existed. Full account in verification-log.md
  section 5, including one CSS fix attempt that measurably made Lighthouse's CLS score worse and was
  reverted rather than kept.
- The live app currently has an empty `Quotes` table (a throwaway verification quote was created, checked,
  and deleted again) - the schema exists and read/write/delete all work, but there's no seed data.
