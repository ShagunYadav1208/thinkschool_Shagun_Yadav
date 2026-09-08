# Deploy runbook

`az stack sub validate` (read-only, no resources touched) has been run successfully against both
environments - see README "Current status" for the real output. **Neither `azd provision` nor
`az stack sub create` has been run** - either would create real resources (`dev`) or bring the
live `syquotes17-rg`/`syquotes19-rg` resources under Deployment Stacks management with
`denyDelete` protection (`prod`), which needs an explicit go-ahead first.

## 0. Prerequisites

```bash
az login
az account show   # confirm the "Azure for Students" subscription is active

azd config set alpha.deployment.stacks on   # one-time, machine-wide azd config
```

## 1. Environments (already created, local-only - see `.azure/*/config.json`)

```bash
cd day-24/piece1

azd env new dev  --location eastasia --subscription 30e5e569-cb36-491a-91c9-2d880095bdcb
azd env set RESOURCE_GROUP_NAME syquotes24dev-rg
azd env set SERVICEBUS_RESOURCE_GROUP_NAME syquotes24dev-rg
azd env set SQL_AAD_ADMIN_NAME "$(az ad signed-in-user show --query userPrincipalName -o tsv)"
azd env set API_APP_NAME syquotes24dev-api
azd env set API_SERVICE_PLAN_NAME syquotes24dev-api-plan
azd env set API_SKU_NAME F1
azd env set API_SKU_TIER Free
azd env set CORS_ALLOWED_ORIGIN http://localhost:4200
azd env set SQL_SERVER_NAME syquotes24dev-sql
azd env set SERVICEBUS_NAMESPACE_NAME syquotes24devsb

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
```

`AZURE_PRINCIPAL_ID` is not set explicitly - azd resolves it automatically at provision time from
the currently `az login`'d identity, same as `AZURE_SUBSCRIPTION_ID`.

## 2. Preview (attempted, not supported - see README "What azd's --preview couldn't do")

```bash
azd provision --preview --environment dev
# ERROR: deployment failed: error deploying infrastructure: preview not supported
```

Fallback used instead - `az stack sub validate` (read-only, genuinely safe):

```bash
az stack sub validate \
  --name day24dev --location eastasia \
  --deny-settings-mode none --action-on-unmanage detachAll \
  --template-file infra/main.bicep \
  --parameters environmentName=dev location=eastasia \
    resourceGroupName=syquotes24dev-rg serviceBusResourceGroupName=syquotes24dev-rg \
    sqlAadAdminObjectId="$(az ad signed-in-user show --query id -o tsv)" \
    sqlAadAdminName="$(az ad signed-in-user show --query userPrincipalName -o tsv)" \
    apiAppName=syquotes24dev-api apiServicePlanName=syquotes24dev-api-plan \
    apiSkuName=F1 apiSkuTier=Free corsAllowedOrigin=http://localhost:4200 \
    sqlServerName=syquotes24dev-sql serviceBusNamespaceName=syquotes24devsb
```

Same command for prod, with `--deny-settings-mode denyDelete` (a live environment should actually
deny deletes) and prod's real parameter values (see README section 3).

## 3. Provision for real (not yet run)

```bash
azd provision --environment dev    # creates: syquotes24dev-rg + everything in it
azd provision --environment prod   # adopts: syquotes17-rg + syquotes19-rg's existing resources
                                    # into a Deployment Stack (denyDelete)
```

`prod` is the interesting case: the resources already exist (day-17/19's manual - well, day-23's
Bicep-but-not-stack - deployment). Running this brings them under stack management for the first
time; ARM updates them in place (same idempotent behavior `what-if` proved in day-23) rather than
recreating anything, then registers the stack and applies `denyDelete`.

## 4. Deploy the API code (not yet run)

```bash
azd deploy api --environment dev
azd deploy api --environment prod
```

Finds the target site via the `azd-service-name: api` tag `modules/api.bicep` sets, inside
whichever resource group `AZURE_RESOURCE_GROUP` (this template's own output) points at for the
active environment.

## 5. Grant the App Service's managed identity access to the database

Unchanged from day-23 - still a T-SQL statement, still not expressible in Bicep/ARM (and Azure
Deployment Stacks doesn't change that - a stack manages ARM resources, not database-level
permissions):

```bash
sqlcmd -S <sqlServerFqdn> -d quotesdb -G -N -C <<'EOF'
CREATE USER [<apiAppName>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<apiAppName>];
ALTER ROLE db_datawriter ADD MEMBER [<apiAppName>];
EOF
```

## 6. Tear down (not yet run - the payoff this whole piece is about)

```bash
azd down --environment dev    # or: az stack sub delete --name day24dev --action-on-unmanage deleteAll
```

One command, one confirmation, and the *entire* stack - resource group, plan, site, SQL server,
database, firewall rule, Service Bus namespace, topic, subscriptions, and the RBAC role
assignment - is gone. No manually re-deriving "what did this exercise create" from memory or
`az resource list` output, which is exactly the teardown problem plain deployments have (see
README "What Deployment Stacks give you").
