# Deploy runbook

Verified end-to-end against real Azure on 2026-09-14 (subscription `ca4adbac-...`, not the
`30e5e569-...` one this file originally referenced - that one is still `Disabled`; always
re-check `az account list -o table` for which subscriptions are actually usable before starting).
Both the backend (`azd`) and frontend (Static Web Apps, deployed separately - `azure.yaml` only
drives the API) came up clean once the fixes below were applied. `infra/main.bicep` and its
modules already have the two real Bicep bugs fixed in source (see "Bugs found and fixed" below) -
nothing to patch there anymore.

## 0. Prerequisites

```bash
az login
az account list -o table   # find a subscription with state "Enabled" - cross-check with:
az rest --method get --url "https://management.azure.com/subscriptions/<id>?api-version=2021-01-01" \
  --query state -o tsv     # az account list/show can both lie about this - this call is authoritative

azd config set alpha.deployment.stacks on   # one-time, machine-wide
```

## 1. Backend environment

```bash
cd day-26/piece1

azd env new dev --location eastasia --subscription <your-enabled-subscription-id>
azd env set RESOURCE_GROUP_NAME syquotes26dev-rg
azd env set SERVICEBUS_RESOURCE_GROUP_NAME syquotes26dev-rg
azd env set SQL_AAD_ADMIN_NAME "$(az ad signed-in-user show --query userPrincipalName -o tsv)"
azd env set API_APP_NAME syquotes26dev-api
azd env set API_SERVICE_PLAN_NAME syquotes26dev-api-plan
# F1/Free works FINE once the CREATE TABLE permission fix in step 4 is applied before your
# first request hits the API - it was only quota-exhausted last time because the app
# crash-looped on that missing permission for the length of a full 20-minute deploy timeout.
# If you'd rather not risk it, use B1/Basic instead - no daily CPU quota, ~$13/mo prorated.
azd env set API_SKU_NAME F1
azd env set API_SKU_TIER Free
azd env set CORS_ALLOWED_ORIGIN http://localhost:4200   # updated again in step 6 once the SWA exists
azd env set SQL_SERVER_NAME syquotes26dev-sql
azd env set SERVICEBUS_NAMESPACE_NAME syquotes26devsb
azd env set KEY_VAULT_NAME syquotes26dev-kv
azd env set REDIS_CONNECTION_STRING_SECRET_VALUE "localhost:6379"   # synthetic placeholder is fine
azd env set LOG_ANALYTICS_WORKSPACE_NAME syquotes26dev-law
azd env set APP_INSIGHTS_NAME syquotes26dev-ai
```

Names must be globally unique (SQL server, Key Vault, Service Bus namespace) - pick a different
suffix if any of these collide.

## 2. Validate (read-only, zero cost)

```bash
az stack sub validate \
  --name day26dev --location eastasia \
  --deny-settings-mode none --action-on-unmanage detachAll \
  --template-file infra/main.bicep \
  --parameters environmentName=dev resourceGroupName=syquotes26dev-rg \
    serviceBusResourceGroupName=syquotes26dev-rg \
    sqlAadAdminObjectId="$(az ad signed-in-user show --query id -o tsv)" \
    sqlAadAdminName="$(az ad signed-in-user show --query userPrincipalName -o tsv)" \
    apiAppName=syquotes26dev-api apiServicePlanName=syquotes26dev-api-plan \
    apiSkuName=F1 apiSkuTier=Free corsAllowedOrigin=http://localhost:4200 \
    sqlServerName=syquotes26dev-sql serviceBusNamespaceName=syquotes26devsb \
    keyVaultName=syquotes26dev-kv redisConnectionStringSecretValue=localhost:6379 \
    logAnalyticsWorkspaceName=syquotes26dev-law appInsightsName=syquotes26dev-ai
```

## 3. Provision the infrastructure

```bash
azd provision --environment dev
```

Creates: resource group, Key Vault, App Insights + Log Analytics, the error-rate alert, App
Service + plan, SQL Server + `quotesdb`, Service Bus namespace + topic. Takes ~3 minutes.

## 4. Grant the API's managed identity database access

Bicep/ARM can't express this - it's T-SQL, run as the AAD admin from step 0. **Needs a temporary
firewall rule for your own IP first** (the SQL server only allows Azure services + explicitly
allow-listed IPs by default):

```bash
MY_IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create -g syquotes26dev-rg -s syquotes26dev-sql \
  -n allow-deploy-machine --start-ip-address $MY_IP --end-ip-address $MY_IP

sqlcmd -S syquotes26dev-sql.database.windows.net -d quotesdb -G -N -C <<'EOF'
CREATE USER [syquotes26dev-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [syquotes26dev-api];
ALTER ROLE db_datawriter ADD MEMBER [syquotes26dev-api];
ALTER ROLE db_ddladmin ADD MEMBER [syquotes26dev-api];
EOF

az sql server firewall-rule delete -g syquotes26dev-rg -s syquotes26dev-sql -n allow-deploy-machine
```

**The `db_ddladmin` line is the one that's easy to miss** (an earlier version of this runbook,
copied from day-23, only had `db_datareader`/`db_datawriter`). `Program.cs` calls
`EnsureCreatedAsync()` against Azure SQL, which issues `CREATE TABLE` on first run - without
`db_ddladmin` that fails with `CREATE TABLE permission denied`, thrown as an unhandled exception
at startup, which crashes the whole host. Confirmed live: this is exactly what burned through the
F1 tier's daily CPU quota on the first real deploy attempt.

## 5. Deploy the API code

```bash
azd deploy api --environment dev
```

## 6. Frontend - Azure Static Web Apps

Not part of `azd`/`azure.yaml` (API only) - deployed directly with the SWA CLI.

```bash
az staticwebapp create -g syquotes26dev-rg -n syquotes26dev-swa --location "East Asia" --sku Free
# note the defaultHostname it prints, e.g. orange-smoke-028b17000.3.azurestaticapps.net
```

**Entra ID app registration for the SPA** - only reuse the existing `AzureAd`/`msal` app
registration (`fbc2f15a-...` API / `335b9c06-...` SPA, see appsettings.json/environment.ts) if
you're logged in as an **owner** of that app registration (`az ad app owner list --id
335b9c06-58f9-4bc3-8732-b3f16bcf39e6`) - only an owner or tenant admin can edit its redirect URI
allow-list, which you must do before login will work from a new URL. If you're not an owner,
create your own SPA app registration instead (works identically - the API only validates
audience/tenant on the token, not which client app requested it):

```bash
az ad app create --display-name "syquotes26dev-spa" --sign-in-audience AzureADMyOrg \
  --is-fallback-public-client true --query appId -o tsv
# then wire up redirect URIs + the API's scope via Graph (az ad app update doesn't support
# --spa-redirect-uris in some az cli versions - use az rest if it errors):
SCOPE_ID=$(az ad app show --id fbc2f15a-e32e-4e04-9a55-9dc796093009 \
  --query "api.oauth2PermissionScopes[?value=='access_as_user'].id" -o tsv)
az rest --method PATCH --url "https://graph.microsoft.com/v1.0/applications(appId='<new-app-id>')" \
  --headers "Content-Type=application/json" \
  --body "{\"spa\":{\"redirectUris\":[\"https://<your-swa-hostname>\",\"http://localhost:4200\"]},\"requiredResourceAccess\":[{\"resourceAppId\":\"fbc2f15a-e32e-4e04-9a55-9dc796093009\",\"resourceAccess\":[{\"id\":\"$SCOPE_ID\",\"type\":\"Scope\"}]}]}"
```

Update `quotes-list-detail/src/environments/environment.prod.ts`: `apiBaseUrl`/`apiOrigin` to
`https://syquotes26dev-api.azurewebsites.net`, `msal.redirectUri` to your SWA hostname, and
`msal.clientId` to your app registration's id (only if you created a new one).

Update the API's CORS setting to match:
```bash
az webapp config appsettings set -g syquotes26dev-rg -n syquotes26dev-api \
  --settings "Cors__AllowedOrigin=https://<your-swa-hostname>"
```

Build and deploy:
```bash
cd quotes-list-detail
npm run build
DEPLOY_TOKEN=$(az staticwebapp secrets list -g syquotes26dev-rg -n syquotes26dev-swa --query "properties.apiKey" -o tsv)
npx --yes @azure/static-web-apps-cli deploy ./dist/quotes-list-detail/browser \
  --deployment-token "$DEPLOY_TOKEN" --env production
```

## 7. Verify

```bash
curl -i https://syquotes26dev-api.azurewebsites.net/api/quotes   # expect 401 - auth is on, this is correct
# open the SWA hostname in a browser, "Log in with Microsoft", confirm you land in the app
az webapp config appsettings list -g syquotes26dev-rg -n syquotes26dev-api -o table
# confirm Redis__ConnectionString shows @Microsoft.KeyVault(SecretUri=...), never a plaintext value
```

## Bugs found and fixed this session (already applied to source, not just documented here)

1. **`infra/modules/appinsights.bicep`** - added an `appInsightsId` output. `main.bicep` used to
   reconstruct that resource ID with `resourceId('Microsoft.Insights/components', name)` from a
   `subscription`-scoped template with no resource-group context, producing a malformed ID
   (missing the `/resourceGroups/...` segment) - the alert module's `scopes` array then failed
   with `Scopes list contains an invalid resource Id`.
2. **`infra/modules/alerts.bicep`** - the KQL query used `${errorRateThreshold}` inside a
   triple-quoted (`'''`) string. Bicep's multi-line strings don't interpolate - Azure received the
   literal text `${errorRateThreshold}`, curly braces included, and the query parser rejected it.
   Fixed with `concat(...)` instead.
3. **SQL grant script** (this file) - needed `db_ddladmin`, not just `db_datareader`/
   `db_datawriter`, for `EnsureCreatedAsync()`'s initial `CREATE TABLE` to succeed.
4. **`quotes-list-detail/public/staticwebapp.config.json`** - the CSP's `connect-src` allowed only
   `'self'` and `https://*.azurewebsites.net`, not `https://login.microsoftonline.com`. MSAL's
   token-exchange POST to Entra ID was blocked client-side by the browser before it ever left the
   page, surfacing as `post_request_failed`. Added `https://login.microsoftonline.com` to both
   `connect-src` (the token POST) and `frame-src` (MSAL's silent-renewal iframe).

## Teardown

```bash
azd down --environment dev --force --purge   # deletes the whole resource group + purges
                                              # soft-deleted Key Vault/Log Analytics
az ad app delete --id <your-spa-app-id>      # if you created a new app registration in step 6 -
                                              # azd down has no reach into Entra ID
```
