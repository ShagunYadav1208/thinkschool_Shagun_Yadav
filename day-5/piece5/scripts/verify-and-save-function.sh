#!/usr/bin/env bash
# Day 5 Piece 5 — Verify in App Insights with your first KQL.
#
# Runnable end-to-end once Day 5 Piece 4's `azd up` has actually succeeded against a real
# subscription (see ../../piece4). Every command here was checked against the real, installed
# Azure CLI (`az monitor app-insights query --help`, `az monitor log-analytics workspace
# saved-search create --help`) — nothing here is guessed syntax.
#
# Nothing to hardcode or paste as a "token": this authenticates via `az login`'s cached session,
# same as every other script in day-5. Resource names are discovered from the resource group, not
# assumed, since azd's Bicep (day-5/piece4/infra/resources.bicep) names them from a hash
# (`appi-<resourceToken>`, `log-<resourceToken>`) that isn't known ahead of a real deployment.

set -euo pipefail

# `az monitor app-insights query` lives in the `application-insights` extension, which isn't
# installed by default. Without this, az prompts "install it now? (Y/n)" interactively — which
# throws an unhandled EOF traceback in any non-interactive shell instead of a clean error.
# --upgrade makes this idempotent (installs if missing, updates if present, no-ops otherwise).
az extension add --name application-insights --upgrade --yes >/dev/null

# Defaults match Day 5 Piece 4's azd environment name (thinkschool-quotes-api) and its Bicep's
# `rg-${environmentName}` convention (see piece4/infra/main.bicep) — override if yours differ.
RESOURCE_GROUP="${1:-rg-thinkschool-quotes-api}"
APP_NAME="${2:-quotes-api}"
OUT_DIR="$(dirname "$0")"

echo "==> Discovering the App Insights resource + its backing Log Analytics workspace"
APPI_NAME=$(az resource list \
  --resource-group "$RESOURCE_GROUP" \
  --resource-type Microsoft.Insights/components \
  --query "[0].name" --output tsv)

WORKSPACE_RESOURCE_ID=$(az monitor app-insights component show \
  --resource-group "$RESOURCE_GROUP" \
  --app "$APPI_NAME" \
  --query workspaceResourceId --output tsv)
WORKSPACE_NAME="${WORKSPACE_RESOURCE_ID##*/}"

echo "    App Insights: $APPI_NAME"
echo "    Log Analytics workspace: $WORKSPACE_NAME"

echo "==> Fetching the deployed app's live URL"
FQDN=$(az containerapp show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$APP_NAME" \
  --query properties.configuration.ingress.fqdn --output tsv)
echo "    https://$FQDN"

echo "==> Warming up (Container Apps scales to zero when idle; the first hit can 504 while it cold-starts)"
for i in 1 2 3 4 5; do
  code=$(curl -s -o /dev/null -w "%{http_code}" "https://$FQDN/health")
  [ "$code" = "200" ] && break
  echo "    attempt $i: $code, retrying..."
  sleep 10
done

echo "==> Hitting a few endpoints so there's something in 'requests' to query"
curl -s -o /dev/null -w "GET  /health          -> %{http_code}\n" "https://$FQDN/health"
curl -s -o /dev/null -w "GET  /api/quotes      -> %{http_code}\n" "https://$FQDN/api/quotes"
curl -s -o /dev/null -w "POST /api/quotes      -> %{http_code}\n" -X POST "https://$FQDN/api/quotes" \
  -H "Content-Type: application/json" \
  -d '{"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal."}'
curl -s -o /dev/null -w "GET  /api/quotes/{id} -> %{http_code}\n" "https://$FQDN/api/quotes/1"

echo "==> Waiting for App Insights ingestion (typically a few minutes, not instant)"
sleep 180

echo "==> Running the exercise's KQL"
az monitor app-insights query \
  --apps "$APPI_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --analytics-query "requests | where timestamp > ago(30m) | summarize count(), p50=percentile(duration, 50), p99=percentile(duration, 99) by name | order by p99 desc" \
  --output json | tee "$OUT_DIR/query-result.json"

echo "==> Saving it as a reusable function: EndpointLatencySummary(lookback)"
az monitor log-analytics workspace saved-search create \
  --resource-group "$RESOURCE_GROUP" \
  --workspace-name "$WORKSPACE_NAME" \
  --name "EndpointLatencySummary" \
  --category "Performance" \
  --display-name "Endpoint Latency Summary" \
  --saved-query 'requests | where timestamp > ago(lookback) | summarize RequestCount = count(), p50 = percentile(duration, 50), p99 = percentile(duration, 99) by name | order by p99 desc' \
  --func-alias "EndpointLatencySummary" \
  --func-param "lookback:timespan = 30m"

echo "==> Done. Real query result saved to scripts/query-result.json"
echo "    From now on, EndpointLatencySummary() or EndpointLatencySummary(1h) works from any query"
echo "    in this workspace."
