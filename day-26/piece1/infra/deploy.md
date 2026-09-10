# Deploy runbook

`az bicep build` passes clean. **Nothing has been deployed or validated against real Azure** -
this student's subscription is still `Disabled` (spending limit reached, first found during
day-25 - re-confirmed this session, see README "Current status"): even a plain
`az deployment sub validate` with zero Deployment Stacks involvement fails immediately with
`ReadOnlyDisabledSubscription`. Local verification (OTLP -> Jaeger) stood in for what Azure
Monitor would show once this is deployed - see README "Distributed tracing, verified locally."

## 0. Prerequisites

```bash
az login
az account show   # NOTE: this can say "Enabled" even when the subscription resource itself
                   # is Disabled - always cross-check with the command below.
az rest --method get --url "https://management.azure.com/subscriptions/<id>?api-version=2021-01-01" \
  --query state -o tsv   # must NOT say "Disabled" before attempting anything past this point

azd config set alpha.deployment.stacks on   # one-time, machine-wide (already on from day-24/25)
```

## 1. Environments (not yet created for this piece - day-25's dev/prod pattern carries over unchanged)

```bash
cd day-26/piece1

azd env new dev  --location eastasia --subscription 30e5e569-cb36-491a-91c9-2d880095bdcb
azd env set RESOURCE_GROUP_NAME syquotes26dev-rg
azd env set SERVICEBUS_RESOURCE_GROUP_NAME syquotes26dev-rg
azd env set SQL_AAD_ADMIN_NAME "$(az ad signed-in-user show --query userPrincipalName -o tsv)"
azd env set API_APP_NAME syquotes26dev-api
azd env set API_SERVICE_PLAN_NAME syquotes26dev-api-plan
azd env set API_SKU_NAME F1
azd env set API_SKU_TIER Free
azd env set CORS_ALLOWED_ORIGIN http://localhost:4200
azd env set SQL_SERVER_NAME syquotes26dev-sql
azd env set SERVICEBUS_NAMESPACE_NAME syquotes26devsb
azd env set KEY_VAULT_NAME syquotes26dev-kv
azd env set REDIS_CONNECTION_STRING_SECRET_VALUE "<a real value, or a synthetic placeholder>"
azd env set LOG_ANALYTICS_WORKSPACE_NAME syquotes26dev-law
azd env set APP_INSIGHTS_NAME syquotes26dev-ai

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
azd env set REDIS_CONNECTION_STRING_SECRET_VALUE "<a real value, or a synthetic placeholder>" -e prod
azd env set LOG_ANALYTICS_WORKSPACE_NAME syquotes17-law -e prod
azd env set APP_INSIGHTS_NAME syquotes17-ai -e prod
```

## 2. Validate (blocked this session - exact command for once the subscription is back)

```bash
az stack sub validate \
  --name day26dev --location eastasia \
  --deny-settings-mode none --action-on-unmanage detachAll \
  --template-file infra/main.bicep \
  --parameters environmentName=dev resourceGroupName=syquotes26dev-rg ... \
    logAnalyticsWorkspaceName=syquotes26dev-law appInsightsName=syquotes26dev-ai
```

## 3. Provision + deploy for real (not yet run)

```bash
azd provision --environment dev
azd deploy api --environment dev
```

## 4. Local verification (what this session actually did instead - see README)

```bash
docker start jaeger   # jaegertracing/all-in-one, OTLP on 4317/4318, UI on 16686
cd QuotesApi && dotnet run
# create a quote, wait ~2-10s for the outbox relay's next poll tick(s)
# http://localhost:16686 -> QuotesApi service -> find the "POST /api/quotes/" trace
```

## 5. Once deployed for real: verify KQL queries against live data

```bash
# Application Insights blade -> Logs, or:
az monitor app-insights query \
  --app <appInsightsName> -g <resourceGroup> \
  --analytics-query "$(cat ../observability/kql/p50-p99-by-endpoint.kql)"
```

Repeat for `dependency-breakdown.kql` and `error-rate-alert.kql`. `trace-by-operation-id.kql`
needs a real `operation_Id` substituted first (see that file's own header comment for how to find
one).
