# Day 25 / Piece 1 — Identity end-to-end

Backend is [day-24/piece1](../../day-24/piece1)'s `QuotesApi`, copied into [QuotesApi/](QuotesApi/)
and then modified (per this piece's brief) to add real Entra ID app auth. Frontend is
day-24/piece1's Angular app, copied into [quotes-list-detail/](quotes-list-detail/) and modified to
replace its entirely-fake `localStorage` login with real MSAL/Entra ID sign-in. `infra/` is
day-24's Bicep + azd setup, modified to add a Key Vault. Managed Identity for API→SQL and
API→Service Bus is **unchanged** - it was already fully in place as of day-17/19.

## Current status

**The Entra ID app auth and Key Vault implementations are complete and real, but currently
switched OFF by a feature flag** - one line in the backend config, one in each frontend
environment file. This is deliberate, not a shortcut: this student's Azure subscription hit its
spending limit mid-exercise (see "Why it's flagged off" below), and the live app needed to keep
working (open access, no login) for local testing in the meantime. Nothing was deleted - flipping
`Auth:Enabled` / `authEnabled` to `true` and rebuilding is the entire re-enable step.

- `dotnet build` (QuotesApi): 0 errors.
- `npm run build` (quotes-list-detail): clean, 0 warnings.
- `npm test`: 20/20 passing.
- **Live, local proof the auth implementation genuinely works** (captured before it was flagged
  off): every endpoint across every feature added since day-15 returned a real `401` with no token
  and `401` with a garbage token - not simulated, actually run against the running API.
- **Two real Entra ID app registrations exist, live, in this student's own Microsoft Entra
  tenant** (not simulated, not deleted when auth was flagged off) - see section 2.
- **Key Vault (`infra/modules/keyvault.bicep` / `keyvault-rbac.bicep`) is written and compiles
  clean, never deployed** - this student's Azure subscription became `Disabled` (spending limit)
  before it could be validated or provisioned. See "Why it's flagged off."

## Why it's flagged off, not deleted

Two separate problems hit during this exercise:

1. **A real code bug in the login flow.** `app.config.ts` was accidentally constructing two
   separate `PublicClientApplication` instances (one from `MsalModule.forRoot()`'s direct
   argument, one from a duplicate `MSAL_INSTANCE` provider) - whichever instance actually
   processed the return from Microsoft's login page wasn't necessarily the same instance
   `MsalGuard`/`MsalInterceptor` were checking, so it silently looked like "no account found" with
   no thrown error, and immediately bounced back into another login attempt. Fixed with a
   module-level memoized singleton, confirmed in the fixed code below - but this was found and
   fixed *during* live testing, not before.
2. **The Azure subscription's own state changed mid-session:**
   ```
   $ az rest --method get --url ".../subscriptions/30e5e569-...?api-version=2021-01-01"
   "state": "Disabled"
   "subscriptionPolicies": { "spendingLimit": "On" }
   ```
   Standard Azure for Students behavior once the free credit is exhausted. Confirmed
   subscription-wide (even a plain `az deployment sub validate` with no Deployment Stacks
   involvement fails the same way), not specific to this template.

Given both, the user's call was: keep the running app usable and open (no login blocking local
testing) *right now*, but don't throw away real, working implementation - gate it behind a flag
so re-enabling later is a two-line change plus a rebuild, not a rewrite. Both flags default to
`false`; the same session (or a future one) is expected to flip them to `true` the moment this
student's subscription is usable again, re-run the exact same local 401 proof, then pick up the
Key Vault deployment/validation where it left off.

## 1. Layout

```
QuotesApi/                          - day-24's API, +Microsoft.Identity.Web auth (flagged off)
quotes-list-detail/                 - day-24's Angular app, +MSAL sign-in (flagged off)
infra/
  main.bicep                         - day-24's template + Key Vault wiring (section 5)
  modules/
    api.bicep                        - unchanged shape, takes a Redis__ConnectionString
                                        Key Vault-reference app setting from main.bicep
    sql.bicep, servicebus.bicep       - byte-for-byte unchanged from day-23/24 (MI wiring, section 4)
    keyvault.bicep                    - Key Vault + one secret, never deployed
    keyvault-rbac.bicep               - the role assignment, split out - see its own header
                                        comment for why (circular module dependency otherwise)
  deploy.md                           - exact commands, including the ones not yet run
```

## 2. Entra ID app registrations (real, created live, still exist)

```bash
$ az ad app create --display-name "syquotes25-api" --sign-in-audience AzureADMyOrg
{"appId": "fbc2f15a-e32e-4e04-9a55-9dc796093009", ...}

$ az ad app update --id fbc2f15a-... --identifier-uris "api://fbc2f15a-e32e-4e04-9a55-9dc796093009"
$ az rest --method PATCH .../applications/892bac58-... --body '{"api":{"oauth2PermissionScopes":[
    {"value":"access_as_user","type":"User","isEnabled":true, ...}]}}'

$ az ad app create --display-name "syquotes25-spa" --sign-in-audience AzureADMyOrg
{"appId": "335b9c06-58f9-4bc3-8732-b3f16bcf39e6", ...}

$ az rest --method PATCH .../applications/e8332426-... --body \
    '{"spa":{"redirectUris":["http://localhost:4200","https://black-desert-....azurestaticapps.net"]}}'
$ az ad app permission add --id 335b9c06-... --api fbc2f15a-... --api-permissions 8de75c46-...=Scope
```

- **API** (`syquotes25-api`, `fbc2f15a-e32e-4e04-9a55-9dc796093009`): App ID URI
  `api://fbc2f15a-e32e-4e04-9a55-9dc796093009`, one delegated scope `access_as_user`.
- **SPA** (`syquotes25-spa`, `335b9c06-58f9-4bc3-8732-b3f16bcf39e6`): public client, redirect URIs
  for both local dev and the live production Static Web App, delegated permission requesting the
  API's `access_as_user` scope.

**A real finding, not glossed over:** `az ad app permission admin-consent` failed live -
`Authorization_RequestDenied: This operation can only be performed by an administrator` - this
student account isn't a tenant admin. Any user, including this one, can still consent for
*themselves* on first login (a one-time "Access QuotesApi as you" prompt) - it just means consent
happens per-user rather than once for everyone.

**Why these IDs are safe to commit even while disabled:** tenant ID and both client IDs are
public identifiers - Microsoft's own documentation is explicit that only a client *secret* or
certificate is sensitive, never a client ID or tenant ID. Neither app registration here *has* a
client secret: the API only validates tokens, and the SPA is a public client by design (PKCE, no
secret).

## 3. The feature flag itself

**Backend** (`QuotesApi/appsettings.json`):
```json
"Auth": {
  "Enabled": false
},
"AzureAd": {
  "Instance": "https://login.microsoftonline.com/",
  "TenantId": "8d46a076-d093-416d-a57b-8692cde13bf8",
  "ClientId": "fbc2f15a-e32e-4e04-9a55-9dc796093009"
}
```

`Extensions/InfrastructureExtensions.cs` exposes `IsAuthEnabled(configuration)`, checked in two
places - the DI registration (skips `AddMicrosoftIdentityWebApi`/`FallbackPolicy` entirely when
off) and `Program.cs` (skips `UseAuthentication()`/`UseAuthorization()` middleware when off). See
section 4 for the actual auth code, unchanged from when it was live.

**Frontend** (`environment.ts` and `environment.prod.ts`):
```typescript
authEnabled: false,
msal: {
  clientId: '335b9c06-58f9-4bc3-8732-b3f16bcf39e6',
  authority: 'https://login.microsoftonline.com/8d46a076-d093-416d-a57b-8692cde13bf8',
  redirectUri: 'http://localhost:4200', // or the SWA URL in environment.prod.ts
  apiScope: 'api://fbc2f15a-e32e-4e04-9a55-9dc796093009/access_as_user',
},
```

`app.config.ts` **always** registers MSAL's Angular providers (`MsalModule.forRoot`,
`MsalInterceptor`, etc.) - Angular DI has no "provide this only if some runtime condition holds",
so a component injecting `MsalService` while its provider doesn't exist throws `NG0201`. Instead,
only *behavior* is gated: `msalInterceptorConfigFactory()` returns an **empty**
`protectedResourceMap` while `authEnabled` is false, so `MsalInterceptor` has nothing to attach a
token to and passes every request through untouched - functionally identical to not being
registered at all. `quotes.routes.ts` doesn't register the `/login` route or apply `MsalGuard` to
`quotes/:id` while disabled, so nothing in the running app can ever trigger an interactive login.

**To re-enable:** flip both `Auth:Enabled` (backend) and `authEnabled` (both frontend environment
files) to `true`, rebuild both projects. That's the whole change - no other code needs touching.

## 4. MI wiring (unchanged, always active regardless of the flag - pasted in full)

```csharp
/// <summary>
/// Provider choice is driven entirely by the connection string's shape, not an
/// explicit environment check: an Azure SQL connection string contains
/// "Authentication=Active Directory Managed Identity" and no password anywhere -
/// when the app is running as an Azure App Service, Microsoft.Data.SqlClient
/// exchanges that for a token via the App Service's system-assigned managed
/// identity automatically. Locally, the SQLite fallback ("Data Source=quotes.db")
/// needs nothing extra. Either way, no credential is ever read from config,
/// checked into source, or set as an App Service secret.
/// </summary>
public static IServiceCollection AddInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=quotes.db";

    var usesAzureSql = connectionString.Contains("Authentication=Active Directory", StringComparison.OrdinalIgnoreCase);

    services.AddDbContext<QuotesDbContext>(options =>
    {
        if (usesAzureSql)
            options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly("QuotesApi"));
        else
            options.UseSqlite(connectionString);
    });

    // ... CORS setup ...

    // Day 25: Entra ID app auth - feature-flagged, see IsAuthEnabled's doc comment.
    if (IsAuthEnabled(configuration))
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
    }

    services.AddScoped<IQuoteRepository, QuoteRepository>();

    // Day 19: Service Bus, same "zero secrets in config" architecture day-17 used for Azure
    // SQL - no connection string, no key, anywhere, just a fully-qualified namespace (a public
    // DNS name) and a credential.
    //
    // The credential is picked by environment rather than left to DefaultAzureCredential's own
    // fallback chain: confirmed live that in this SDK version, ManagedIdentityCredential's IMDS
    // probe (169.254.169.254) fails with a hard AuthenticationFailedException when there's no
    // metadata endpoint to reach (i.e. anywhere that isn't an actual Azure compute resource),
    // and DefaultAzureCredential does not fall through to AzureCliCredential after that - it
    // just throws. So Development explicitly uses AzureCliCredential (the locally-logged-in
    // `az` session); everywhere else uses DefaultAzureCredential, where an App Service's
    // managed identity actually is reachable via IMDS and this problem doesn't occur.
    var serviceBusOptions = configuration.GetSection(ServiceBusOptions.SectionName).Get<ServiceBusOptions>()
        ?? throw new InvalidOperationException("Missing ServiceBus configuration section.");

    TokenCredential serviceBusCredential = environment.IsDevelopment()
        ? new AzureCliCredential()
        : new DefaultAzureCredential();

    services.AddSingleton(serviceBusOptions);
    services.AddSingleton(_ => new ServiceBusClient(serviceBusOptions.FullyQualifiedNamespace, serviceBusCredential));
    // ...
}
```

Two managed identities are actually in play here: **the App Service's system-assigned managed
identity** (`DefaultAzureCredential` in production) for both Service Bus and Azure SQL's
`Authentication=Active Directory Managed Identity` auth mode, and **the developer's own signed-in
identity** (`AzureCliCredential`) for Service Bus in local development. Neither path has ever had
a password, connection-string secret, or key anywhere in this repo, in any of day-17 through
day-25 - and this is completely independent of the app-auth feature flag above; MI wiring is
always active.

## 5. Key Vault reference (written, not yet deployed)

**`infra/modules/keyvault.bicep`:**

```bicep
resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: vaultName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource redisSecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  parent: vault
  name: 'redis-connection-string'
  properties: {
    value: redisConnectionStringSecretValue
  }
}
```

**The reference itself**, in `main.bicep`'s `api` module call:

```bicep
{
  name: 'Redis__ConnectionString'
  value: '@Microsoft.KeyVault(SecretUri=${keyVault.outputs.redisSecretUri})'
}
```

The App Service platform resolves this before the app ever starts - `QuotesApi`'s own code just
reads `configuration["Redis:ConnectionString"]` and has no idea Key Vault is involved.

**The access grant** (`infra/modules/keyvault-rbac.bicep`, split from `keyvault.bicep` specifically
to avoid a circular module dependency - `api` needs `keyvault`'s secret URI output, this needs
`api`'s principalId output):

```bicep
resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: vaultName
}

resource secretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, readerPrincipalId, keyVaultSecretsUserRoleId)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: readerPrincipalId
    principalType: 'ServicePrincipal'
  }
}
```

`az bicep build --file infra/main.bicep` compiles this whole graph clean. Never validated or
deployed against real Azure - the subscription went `Disabled` before that step.

## 6. Proving zero plaintext secrets in app settings (static proof)

| Setting | Value shape | Secret? |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `"Production"` | No - an environment name |
| `Cors__AllowedOrigin` | a public HTTPS hostname | No |
| `Redis__ConnectionString` | `@Microsoft.KeyVault(SecretUri=https://...)` | **No** - a pointer, not the secret value |
| `ConnectionStrings__DefaultConnection` | `Server=...;Authentication=Active Directory Managed Identity;...` | No - an auth mode, no password |
| `ServiceBus__FullyQualifiedNamespace` etc. | a public DNS name + topic/subscription names | No |
| `AzureAd__TenantId` / `AzureAd__ClientId` | GUIDs | No - see section 2 |
| `Auth__Enabled` | `"false"` | No - a feature flag |

Nothing here is a plaintext secret, by construction of the template/config themselves. What a live
run would additionally confirm (queued in `infra/deploy.md`, pending subscription reactivation):
that the Key Vault reference actually *resolves* rather than sitting in a broken state.

## 7. Local testing (real, captured before auth was flagged off)

```
$ curl -s -o /dev/null -w "HTTP %{http_code}\n" http://localhost:5116/api/quotes/
HTTP 401
$ curl -s -o /dev/null -w "HTTP %{http_code}\n" -H "Authorization: Bearer garbage-not-a-real-jwt" http://localhost:5116/api/quotes/
HTTP 401
$ curl -s -o /dev/null -w "HTTP %{http_code}\n" http://localhost:5116/api/jobs
HTTP 401
$ curl -s -o /dev/null -w "HTTP %{http_code}\n" http://localhost:5116/api/cache/metrics
HTTP 401
```

Every one of these was a `200` before this piece (day-24's own README: *"confirmed live - no 401
from any endpoint"*). `FallbackPolicy` reaching endpoint groups added in five separate earlier
days, with zero changes to any of those endpoint files, was the concrete payoff of "secure by
default" - this is exactly what re-flipping `Auth:Enabled` restores.

**With the flag off (current state):**
```
$ curl -s -o /dev/null -w "HTTP %{http_code}\n" http://localhost:5116/api/quotes/
HTTP 200
```
Open access, matching the running app today.

## What did I learn this session?

1. **A feature flag is a cheap way to reconcile two legitimately conflicting needs** - "keep the
   real implementation" and "don't block the running app on something outside my control" aren't
   actually in tension once you separate *code existing* from *code being active*. Deleting and
   later rewriting would have thrown away working, tested code over what turned out to be a
   temporary external blocker.
2. **Angular DI's lack of conditional providers forces a specific pattern for optional
   features**: register everything, gate behavior. Trying to conditionally exclude MSAL's
   providers based on a flag would have made every component that injects `MsalService`
   (including this app's own `AuthService`) throw `NG0201` the moment the flag was off - the
   `protectedResourceMap`/`MsalGuard`-in-routes approach avoids ever needing a provider that
   isn't there.
3. **A circular module dependency in Bicep is a real design signal, not just a compiler
   complaint** - `api` needing `keyVault`'s output while `keyVault`'s role assignment needing
   `api`'s output is exactly the shape of "two things need each other" that should never live in
   the same module; splitting the role assignment into its own module (resolving the vault via
   `existing` rather than creating it) mirrors this app's actual identity graph.

## What would break this

- **The deleted Bearer-header unit test leaves a real gap** - nothing in this repo's automated
  test suite asserts that `MsalInterceptor` attaches a token to outgoing requests once re-enabled;
  a future regression that silently stopped attaching tokens would only be caught by the API's own
  401s, not by this test suite.
- **Both flags must be flipped together, by hand, in two different files and languages** - nothing
  enforces that `Auth:Enabled` (C#/JSON) and `authEnabled` (TypeScript, in *two* files) stay in
  sync. Enabling only the backend would make every API call fail with 401 for a frontend that
  never attaches a token; enabling only the frontend would attempt logins against an API that
  accepts anyone anyway.
- **The Key Vault reference has never actually been deployed** - "does `@Microsoft.KeyVault(...)`
  actually resolve against this exact vault/secret/RBAC combination" is unverified beyond
  `az bicep build` succeeding syntactically.
- **The subscription-disabled blocker is entirely external** - nothing in this repo caused it or
  can fix it (needs a renewal, a payment method, or a support request). Whoever picks this up
  should check `az account show`'s `state` before assuming a stale "it worked before" is still
  true.
- **Tenant-wide admin consent still isn't granted** - once re-enabled, the first user to sign in
  (this student) will see a one-time self-consent prompt; anyone lacking permission to consent for
  themselves (a locked-down tenant policy, unlike this one) would be blocked entirely without an
  admin pre-consenting on their behalf.

## Notes for mentor

- `day-24/piece1` was the copy source for `QuotesApi/` and `quotes-list-detail/` - both were
  genuinely modified in place per this piece's brief. Only `sql.bicep`/`servicebus.bicep` in
  `infra/modules/` are byte-for-byte carried over unchanged.
- **Two real Entra ID app registrations exist in this student's own tenant** - see section 2.
  Nothing was simulated there, and nothing was deleted when auth was flagged off.
- **The Azure subscription became disabled (spending limit reached) mid-session** - see "Why it's
  flagged off" and `infra/deploy.md` for exactly what's still outstanding once it's reactivated.
- **The implementation itself is fully working, proven locally** (section 7's before/after), just
  intentionally inactive in the currently-running app at this student's request, pending Azure
  access.

## GitHub link

Not pushed yet - link to follow once pushed to the `thinkbridge-thinkschool` org, per this user's
standing preference that git actions (including read-only ones) need explicit permission each
time.
