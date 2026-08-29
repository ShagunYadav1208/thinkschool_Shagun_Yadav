# Brief to the agent (Claude Code)

**Target:** the Angular app already built in `day-16/piece2` (routing/guards + signals-based
`QuoteManagementStore`), copied unmodified into `day-17/piece1/quotes-list-detail`, deployed live to
Azure Static Web Apps on the default `*.azurestaticapps.net` hostname - no custom domain (I don't own
one; using a real domain isn't a requirement I can honestly claim otherwise).

**Real Week-1 API it must keep calling, unchanged in shape:** `QuotesApi`
(`day-1/piece3/QuotesApi`, copied into `day-17/piece1/QuotesApi` so it can be modified for Azure without
touching the read-only original):
- `GET /api/quotes/?page={page}&size={size}` - paginated list, `[{id,author,text}]`.
- `GET /api/quotes/{id}` - single quote, `404` empty body if missing.
- `POST /api/quotes/` - create, `201`.
- `DELETE /api/quotes/{id}` - `204`, or `404` if already gone.

**Auth requirement: Managed Identity, zero secret in the repo or app settings - not a workaround.**
Managed Identity is an Azure-to-Azure trust mechanism; it doesn't exist for a browser. So the honest
architecture is:
1. `QuotesApi` moves from local SQLite to Azure SQL Database. The App Service hosting it gets a
   system-assigned managed identity.
2. The connection string in `appsettings.Production.json` is
   `Authentication=Active Directory Managed Identity` with **no username, no password** - just a server
   name and that auth mode. `Microsoft.Data.SqlClient` exchanges the App Service's own identity for a
   database token automatically; nothing in code or config ever holds a credential.
3. Azure SQL itself is Azure-AD-only (`azureADOnlyAuthentication: true`) - there is no SQL-auth admin
   login to leak in the first place, not just one that's hidden well.
4. The browser (SWA-hosted Angular app) still calls the API over plain HTTPS + CORS, same as every prior
   day - MI secures the API-to-database hop, not the browser-to-API hop, which is the part of "Managed
   Identity, no client secret" that's easy to state wrong.

**CI/CD, same "no secret" bar:** the API's GitHub Actions deploy uses OIDC federated credentials
(`azure/login` with a federated-credential subject match on this repo+branch), not a stored publish
profile or client secret. The frontend's SWA deploy uses a scoped deployment token (can only push to
this one Static Web App - not a subscription-level credential).

**Deliverable gate: Lighthouse >= 95.** Interpreted as all four categories (performance, accessibility,
best-practices, SEO), measured against the live URL - not a local approximation, though a local
pre-deploy pass is used below as an early signal and to catch fixable issues before spending a real
deploy cycle on them.

Do not modify `day-1/piece3/QuotesApi`, `day-16/piece1`, or `day-16/piece2` - all three are read-only
reference / unmodified copy source, same rule every prior day has used. Any file needed from them gets
copied into `day-17/piece1` first, never edited in place.

**Explicit hold, from the person directing this session:** do not push to GitHub and do not provision
real Azure resources yet - prepare everything (code, IaC, CI/CD config, verification plan) for review
first. See `README.md` "Current status" for exactly what that split is.
