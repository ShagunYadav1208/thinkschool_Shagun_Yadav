# Deploy runbook

`az bicep build` and `az stack sub validate` (read-only, no resources touched) have been run
successfully - see README "Current status" for the real output. **Neither `azd provision` nor
`az stack sub create` has been run** - either would create real resources (`dev`) or modify the
live `syquotes17-rg`/`syquotes19-rg` resources (`prod`), which needs an explicit go-ahead first.

## 0. Prerequisites

```bash
az login
az account show   # confirm the "Azure for Students" subscription is active

azd config set alpha.deployment.stacks on   # one-time, machine-wide azd config (already on from day-24)
```

## 1. Entra ID app registrations (already created for real - see README section 2)

Two real, live Entra ID app registrations exist in this student's own tenant
(`8d46a076-d093-416d-a57b-8692cde13bf8`) - not simulated:

- `syquotes25-api` (`fbc2f15a-e32e-4e04-9a55-9dc796093009`) - exposes one delegated scope,
  `access_as_user`, at App ID URI `api://fbc2f15a-e32e-4e04-9a55-9dc796093009`.
- `syquotes25-spa` (`335b9c06-58f9-4bc3-8732-b3f16bcf39e6`) - a public SPA client, redirect URIs
  `http://localhost:4200` (dev) and `https://black-desert-0fde3f100.7.azurestaticapps.net` (prod),
  with a delegated permission requesting the API's `access_as_user` scope.

Both `TenantId` and both `ClientId`s are committed in source (`QuotesApi/appsettings.json`,
`quotes-list-detail/src/environments/environment*.ts`) - see README "Why these IDs are safe to
commit." Recreate them yourself with:

```bash
az ad app create --display-name "<name>-api" --sign-in-audience AzureADMyOrg
az ad app update --id <api-app-id> --identifier-uris "api://<api-app-id>"
# then PATCH api.oauth2PermissionScopes via Microsoft Graph - see README section 2 for the exact body
az ad app create --display-name "<name>-spa" --sign-in-audience AzureADMyOrg
# then PATCH spa.redirectUris via Microsoft Graph, and `az ad app permission add`/`grant`
```

## 2. Environments

```bash
cd day-25/piece1

azd env new dev  --location eastasia --subscription 30e5e569-cb36-491a-91c9-2d880095bdcb
azd env set RESOURCE_GROUP_NAME syquotes25dev-rg
azd env set SERVICEBUS_RESOURCE_GROUP_NAME syquotes25dev-rg
azd env set SQL_AAD_ADMIN_NAME "$(az ad signed-in-user show --query userPrincipalName -o tsv)"
azd env set API_APP_NAME syquotes25dev-api
azd env set API_SERVICE_PLAN_NAME syquotes25dev-api-plan
azd env set API_SKU_NAME F1
azd env set API_SKU_TIER Free
azd env set CORS_ALLOWED_ORIGIN http://localhost:4200
azd env set SQL_SERVER_NAME syquotes25dev-sql
azd env set SERVICEBUS_NAMESPACE_NAME syquotes25devsb
azd env set KEY_VAULT_NAME syquotes25dev-kv
azd env set REDIS_CONNECTION_STRING_SECRET_VALUE "<a real value, or a synthetic placeholder - never commit this>"

azd env new prod --location eastasia --subscription 30e5e569-cb36-491a-91c9-2d880095bdcb
azd env set RESOURCE_GROUP_NAME syquotes17-rg -e prod
azd env set SERVICEBUS_RESOURCE_GROUP_NAME syquotes19-rg -e prod
azd env set SQL_AAD_ADMIN_NAME "$(az ad signed-in-user show --query userPrincipalName -o tsv)" -e prod
azd env set API_APP_NAME syquotes17-api -e prod
azd env set API_SERVICE_PLAN_NAME syquotes17-plan -e prod
azd env set API_SKU_NAME B1 -e prod
azd env set API_SKU_TIER Basic -e prod
azd env set CORS_ALLOWED_ORIGIN https://black-desert-0fde3f100.7.azurestaticapps.net -e prod
azd env set SQL_SERVER_NAME syquotes17-sql -e prod
azd env set SERVICEBUS_NAMESPACE_NAME syquotes19sb -e prod
azd env set KEY_VAULT_NAME syquotes17-kv -e prod
azd env set REDIS_CONNECTION_STRING_SECRET_VALUE "<a real value, or a synthetic placeholder - never commit this>" -e prod
```

## 3. Validate (safe, read-only - `azd provision --preview` still doesn't support Deployment
Stacks as of day-24; same `az stack sub validate` fallback used there)

```bash
az stack sub validate \
  --name day25dev --location eastasia \
  --deny-settings-mode none --action-on-unmanage detachAll \
  --template-file infra/main.bicep \
  --parameters environmentName=dev resourceGroupName=syquotes25dev-rg ... \
    redisConnectionStringSecretValue="<placeholder>"
```

Same command for prod, with `--deny-settings-mode denyDelete` and prod's real parameter values.

## 4. Provision + deploy for real (not yet run)

```bash
azd provision --environment dev
azd deploy api --environment dev
azd provision --environment prod
azd deploy api --environment prod
```

## 5. Grant the App Service's managed identity access to the database

Unchanged from day-23/24 - still a T-SQL statement, not expressible in Bicep/ARM:

```bash
sqlcmd -S <sqlServerFqdn> -d quotesdb -G -N -C <<'EOF'
CREATE USER [<apiAppName>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<apiAppName>];
ALTER ROLE db_datawriter ADD MEMBER [<apiAppName>];
EOF
```

## 6. Grant tenant-wide admin consent for the SPA's delegated permission (optional)

Not done in this session - this student's account isn't a tenant admin (`az ad app permission
admin-consent` returned `Authorization_RequestDenied`, confirmed live - see README section 2).
Without it, each user who signs in sees a one-time "Access QuotesApi as you" consent prompt on
first login, which they can approve themselves (this tenant's authorization policy has
`allowedToCreateApps: true` and does not block user consent for non-admin-restricted
permissions - confirmed via `az rest --url .../policies/authorizationPolicy`). An admin with the
right role could instead run:

```bash
az ad app permission admin-consent --id 335b9c06-58f9-4bc3-8732-b3f16bcf39e6
```

to pre-consent for every user in the tenant at once.

## 7. Verify - prove zero plaintext secrets in app settings

```bash
az webapp config appsettings list -n <apiAppName> -g <resourceGroup> -o table
```

Expected: `Redis__ConnectionString` shows the literal string
`@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/redis-connection-string/)`
- a pointer, not a secret. Confirm it actually *resolved* (not stuck failing to fetch):

```bash
az webapp config appsettings list -n <apiAppName> -g <resourceGroup> \
  --query "[?name=='Redis__ConnectionString']"
```

A Key Vault reference that fails to resolve shows up with a distinct error state in the Portal's
"Configuration" blade (and in the Kudu `WEBSITE_KEYVAULT_REFERENCES` diagnostic endpoint) rather
than silently falling back to empty - so "the app still starts and Redis just isn't configured"
would look different from "the Key Vault reference is broken," and this step is what tells them
apart.
