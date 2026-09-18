#!/usr/bin/env bash
# One-command deploy wrapper for day-26/piece1 (backend + frontend), built from the runbook in
# deploy.md after a real, verified end-to-end run on 2026-09-14. Chains every step that run
# required - infra provision, the SQL grant Bicep can't express, backend code deploy, the
# Static Web App, Entra ID app-registration wiring, frontend build/deploy - into one command.
#
# What this can't do for you: pick which Azure subscription/identity to use, or decide whether
# you own the existing SPA app registration. Those are read from your current `az login` session
# and checked automatically; the script tells you what it found and picks a safe path (create its
# own app registration) rather than guessing wrong. Override any name below via environment
# variables, e.g.: RESOURCE_GROUP_NAME=myrg ./deploy.sh
#
# Usage:
#   ./deploy.sh            deploy everything
#   ./deploy.sh --down     tear everything down (azd down + delete any app registration this
#                          script created - see .deploy-state for what that was)
set -euo pipefail

# ---- Configuration (override via env vars) --------------------------------------------------
AZD_ENV_NAME="${AZD_ENV_NAME:-dev}"
LOCATION="${LOCATION:-eastasia}"
SWA_LOCATION="${SWA_LOCATION:-East Asia}"
RESOURCE_GROUP_NAME="${RESOURCE_GROUP_NAME:-syquotes26dev-rg}"
API_APP_NAME="${API_APP_NAME:-syquotes26dev-api}"
API_SERVICE_PLAN_NAME="${API_SERVICE_PLAN_NAME:-syquotes26dev-api-plan}"
API_SKU_NAME="${API_SKU_NAME:-F1}"
API_SKU_TIER="${API_SKU_TIER:-Free}"
SQL_SERVER_NAME="${SQL_SERVER_NAME:-syquotes26dev-sql}"
SERVICEBUS_NAMESPACE_NAME="${SERVICEBUS_NAMESPACE_NAME:-syquotes26devsb}"
KEY_VAULT_NAME="${KEY_VAULT_NAME:-syquotes26dev-kv}"
LOG_ANALYTICS_WORKSPACE_NAME="${LOG_ANALYTICS_WORKSPACE_NAME:-syquotes26dev-law}"
APP_INSIGHTS_NAME="${APP_INSIGHTS_NAME:-syquotes26dev-ai}"
SWA_NAME="${SWA_NAME:-syquotes26dev-swa}"
REDIS_CONNECTION_STRING_SECRET_VALUE="${REDIS_CONNECTION_STRING_SECRET_VALUE:-localhost:6379}"

# Existing Entra ID app registrations from earlier exercises - reused only if you own them.
API_APP_ID="${API_APP_ID:-fbc2f15a-e32e-4e04-9a55-9dc796093009}"
API_SCOPE_VALUE="${API_SCOPE_VALUE:-access_as_user}"
EXISTING_SPA_APP_ID="${EXISTING_SPA_APP_ID:-335b9c06-58f9-4bc3-8732-b3f16bcf39e6}"
NEW_SPA_DISPLAY_NAME="${NEW_SPA_DISPLAY_NAME:-${API_APP_NAME}-spa}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
STATE_FILE="$SCRIPT_DIR/.deploy-state"

log() { echo -e "\n\033[1;36m==> $*\033[0m"; }
die() { echo -e "\033[1;31mERROR: $*\033[0m" >&2; exit 1; }

# ---- Teardown mode --------------------------------------------------------------------------
if [[ "${1:-}" == "--down" ]]; then
  log "Tearing down azd-managed resources (resource group $RESOURCE_GROUP_NAME and everything in it)"
  (cd "$PROJECT_DIR" && azd down --environment "$AZD_ENV_NAME" --force --purge)

  if [[ -f "$STATE_FILE" ]]; then
    # shellcheck disable=SC1090
    source "$STATE_FILE"
    if [[ -n "${CREATED_SPA_APP_ID:-}" ]]; then
      log "Deleting the app registration this script created ($CREATED_SPA_APP_ID)"
      az ad app delete --id "$CREATED_SPA_APP_ID" || echo "  (already gone or delete failed - check manually)"
    fi
    rm -f "$STATE_FILE"
  fi
  log "Done."
  exit 0
fi

# ---- 0. Prerequisites ------------------------------------------------------------------------
log "Checking prerequisites"
command -v az >/dev/null || die "az CLI not found"
command -v azd >/dev/null || die "azd CLI not found"
command -v sqlcmd >/dev/null || die "sqlcmd not found (needed for the SQL permission grant)"
command -v npm >/dev/null || die "npm not found (needed to build the frontend)"

SUBSCRIPTION_ID="$(az account show --query id -o tsv)" || die "not logged in - run 'az login' first"
SUB_STATE="$(az rest --method get --url "https://management.azure.com/subscriptions/$SUBSCRIPTION_ID?api-version=2021-01-01" --query state -o tsv)"
[[ "$SUB_STATE" == "Enabled" ]] || die "subscription $SUBSCRIPTION_ID is '$SUB_STATE', not Enabled - switch with 'az account set --subscription <id>'"
log "Using subscription $SUBSCRIPTION_ID (confirmed Enabled)"

azd config set alpha.deployment.stacks on >/dev/null

# ---- 1. azd environment -----------------------------------------------------------------------
log "Setting up azd environment '$AZD_ENV_NAME'"
cd "$PROJECT_DIR"
# Always start from a fresh local environment rather than reusing one that might already exist.
# Real bug hit live: after a teardown.sh run deletes the Azure deployment stack, a *local* env
# left over from before that teardown still remembers its name/state - reusing it makes the next
# `azd provision` try to UPDATE that now-deleted stack instead of creating a new one, which fails
# with "ResourceNotFound: azd-stack-dev was not found" *after* it has already gone ahead and
# created/updated every resource underneath, leaving them orphaned with no stack tracking at all.
# Since this script re-sets every variable via `azd env set` below regardless, there's nothing lost
# by always recreating the local env from scratch - it costs nothing and removes this whole class
# of stale-state bug.
if azd env list | grep -q "^${AZD_ENV_NAME} "; then
  azd env remove "$AZD_ENV_NAME" --force
fi
azd env new "$AZD_ENV_NAME" --location "$LOCATION" --subscription "$SUBSCRIPTION_ID"

SQL_ADMIN_UPN="$(az ad signed-in-user show --query userPrincipalName -o tsv)"
azd env set RESOURCE_GROUP_NAME "$RESOURCE_GROUP_NAME"
azd env set SERVICEBUS_RESOURCE_GROUP_NAME "$RESOURCE_GROUP_NAME"
azd env set SQL_AAD_ADMIN_NAME "$SQL_ADMIN_UPN"
azd env set API_APP_NAME "$API_APP_NAME"
azd env set API_SERVICE_PLAN_NAME "$API_SERVICE_PLAN_NAME"
azd env set API_SKU_NAME "$API_SKU_NAME"
azd env set API_SKU_TIER "$API_SKU_TIER"
azd env set CORS_ALLOWED_ORIGIN "http://localhost:4200"   # updated for real after the SWA exists
azd env set SQL_SERVER_NAME "$SQL_SERVER_NAME"
azd env set SERVICEBUS_NAMESPACE_NAME "$SERVICEBUS_NAMESPACE_NAME"
azd env set KEY_VAULT_NAME "$KEY_VAULT_NAME"
azd env set REDIS_CONNECTION_STRING_SECRET_VALUE "$REDIS_CONNECTION_STRING_SECRET_VALUE"
azd env set LOG_ANALYTICS_WORKSPACE_NAME "$LOG_ANALYTICS_WORKSPACE_NAME"
azd env set APP_INSIGHTS_NAME "$APP_INSIGHTS_NAME"

# ---- 2. Provision -------------------------------------------------------------------------
log "Provisioning infrastructure (azd provision) - this takes a few minutes"
azd provision --environment "$AZD_ENV_NAME" --no-prompt

SQL_SERVER_FQDN="$(azd env get-values --environment "$AZD_ENV_NAME" | grep '^sqlServerFqdn=' | cut -d'"' -f2)"
[[ -n "$SQL_SERVER_FQDN" ]] || die "could not read sqlServerFqdn from azd outputs"

# ---- 3. Grant the API's managed identity database access ------------------------------------
log "Granting SQL permissions to the API's managed identity"
MY_IP="$(curl -s https://api.ipify.org)"
FIREWALL_RULE_NAME="deploy-script-$(date +%s)"
az sql server firewall-rule create -g "$RESOURCE_GROUP_NAME" -s "$SQL_SERVER_NAME" \
  -n "$FIREWALL_RULE_NAME" --start-ip-address "$MY_IP" --end-ip-address "$MY_IP" -o none

cleanup_firewall_rule() {
  az sql server firewall-rule delete -g "$RESOURCE_GROUP_NAME" -s "$SQL_SERVER_NAME" -n "$FIREWALL_RULE_NAME" -o none 2>/dev/null || true
}
trap cleanup_firewall_rule EXIT

sleep 10   # firewall rule propagation
# CREATE USER is guarded (IF NOT EXISTS) so re-running this script after a code-only change - no
# infra change, same database, same managed identity - doesn't fail on "user already exists". The
# ALTER ROLE ... ADD MEMBER lines are already naturally idempotent in SQL Server even without a
# guard - adding an existing member again is a silent no-op, not an error.
sqlcmd -S "$SQL_SERVER_FQDN" -d quotesdb -G -N -C <<EOF
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$API_APP_NAME')
    CREATE USER [$API_APP_NAME] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [$API_APP_NAME];
ALTER ROLE db_datawriter ADD MEMBER [$API_APP_NAME];
ALTER ROLE db_ddladmin ADD MEMBER [$API_APP_NAME];
EOF
# db_ddladmin matters: Program.cs's EnsureCreatedAsync() issues CREATE TABLE on first run against
# an empty database - without it the app throws unhandled and the host crashes at startup.

cleanup_firewall_rule
trap - EXIT

# ---- 4. Deploy the API code ------------------------------------------------------------------
log "Deploying API code (azd deploy)"
azd deploy api --environment "$AZD_ENV_NAME" --no-prompt

# ---- 5. Static Web App ------------------------------------------------------------------------
log "Creating (or reusing) the Static Web App"
if az staticwebapp show -g "$RESOURCE_GROUP_NAME" -n "$SWA_NAME" >/dev/null 2>&1; then
  echo "  $SWA_NAME already exists, reusing it"
else
  az staticwebapp create -g "$RESOURCE_GROUP_NAME" -n "$SWA_NAME" --location "$SWA_LOCATION" --sku Free -o none
fi
SWA_HOSTNAME="$(az staticwebapp show -g "$RESOURCE_GROUP_NAME" -n "$SWA_NAME" --query defaultHostname -o tsv)"
SWA_URL="https://$SWA_HOSTNAME"
log "Static Web App: $SWA_URL"

# ---- 6. Entra ID app registration for the SPA -------------------------------------------------
log "Wiring up the SPA's Entra ID app registration"
MY_OBJECT_ID="$(az ad signed-in-user show --query id -o tsv)"
SPA_APP_ID=""
CREATED_SPA_APP_ID=""

if az ad app owner list --id "$EXISTING_SPA_APP_ID" --query "[].id" -o tsv 2>/dev/null | grep -q "$MY_OBJECT_ID"; then
  echo "  You own the existing app registration ($EXISTING_SPA_APP_ID) - reusing it"
  SPA_APP_ID="$EXISTING_SPA_APP_ID"
  EXISTING_URIS="$(az ad app show --id "$SPA_APP_ID" --query "spa.redirectUris" -o json)"
  if ! echo "$EXISTING_URIS" | grep -q "$SWA_URL"; then
    NEW_URIS="$(echo "$EXISTING_URIS" | python3 -c "import json,sys; u=json.load(sys.stdin); u.append('$SWA_URL'); print(json.dumps(u))" 2>/dev/null \
      || echo "$EXISTING_URIS" | sed "s#\]#,\"$SWA_URL\"]#")"
    az rest --method PATCH --url "https://graph.microsoft.com/v1.0/applications(appId='$SPA_APP_ID')" \
      --headers "Content-Type=application/json" --body "{\"spa\":{\"redirectUris\":$NEW_URIS}}" -o none
  fi
else
  # Reuse a previously-created-by-this-script app registration if one already exists (idempotency).
  EXISTING="$(az ad app list --display-name "$NEW_SPA_DISPLAY_NAME" --query "[0].appId" -o tsv 2>/dev/null || true)"
  if [[ -n "$EXISTING" && "$EXISTING" != "null" ]]; then
    echo "  Reusing previously-created app registration $NEW_SPA_DISPLAY_NAME ($EXISTING)"
    SPA_APP_ID="$EXISTING"
  else
    echo "  You don't own $EXISTING_SPA_APP_ID - creating a new app registration ($NEW_SPA_DISPLAY_NAME)"
    SPA_APP_ID="$(az ad app create --display-name "$NEW_SPA_DISPLAY_NAME" --sign-in-audience AzureADMyOrg --is-fallback-public-client true --query appId -o tsv)"
    CREATED_SPA_APP_ID="$SPA_APP_ID"
  fi

  SCOPE_ID="$(az ad app show --id "$API_APP_ID" --query "api.oauth2PermissionScopes[?value=='$API_SCOPE_VALUE'].id" -o tsv)"
  [[ -n "$SCOPE_ID" ]] || die "could not find scope '$API_SCOPE_VALUE' on API app $API_APP_ID"

  az rest --method PATCH --url "https://graph.microsoft.com/v1.0/applications(appId='$SPA_APP_ID')" \
    --headers "Content-Type=application/json" \
    --body "{\"spa\":{\"redirectUris\":[\"$SWA_URL\",\"http://localhost:4200\",\"http://localhost:4200/\"]},\"requiredResourceAccess\":[{\"resourceAppId\":\"$API_APP_ID\",\"resourceAccess\":[{\"id\":\"$SCOPE_ID\",\"type\":\"Scope\"}]}]}" \
    -o none
fi

TENANT_ID="$(az account show --query tenantId -o tsv)"

if [[ -n "$CREATED_SPA_APP_ID" ]]; then
  echo "CREATED_SPA_APP_ID=$CREATED_SPA_APP_ID" > "$STATE_FILE"
fi

# ---- 7. Point the frontend at real values, update CORS -----------------------------------------
log "Updating environment.prod.ts and the API's CORS setting"
ENV_PROD_FILE="$PROJECT_DIR/quotes-list-detail/src/environments/environment.prod.ts"
API_URL="https://${API_APP_NAME}.azurewebsites.net"

sed -i \
  -e "s#apiBaseUrl:.*#apiBaseUrl: '${API_URL}/api/quotes/',#" \
  -e "s#apiOrigin:.*#apiOrigin: '${API_URL}',#" \
  -e "s#redirectUri:.*#redirectUri: '${SWA_URL}',#" \
  -e "s#clientId:.*#clientId: '${SPA_APP_ID}',#" \
  -e "s#login.microsoftonline.com/[a-f0-9-]*#login.microsoftonline.com/${TENANT_ID}#" \
  "$ENV_PROD_FILE"

az webapp config appsettings set -g "$RESOURCE_GROUP_NAME" -n "$API_APP_NAME" \
  --settings "Cors__AllowedOrigin=$SWA_URL" -o none

# ---- 8. Build and deploy the frontend -----------------------------------------------------
log "Building and deploying the frontend"
(cd "$PROJECT_DIR/quotes-list-detail" && npm run build)
SWA_DEPLOY_TOKEN="$(az staticwebapp secrets list -g "$RESOURCE_GROUP_NAME" -n "$SWA_NAME" --query "properties.apiKey" -o tsv)"
npx --yes @azure/static-web-apps-cli deploy "$PROJECT_DIR/quotes-list-detail/dist/quotes-list-detail/browser" \
  --deployment-token "$SWA_DEPLOY_TOKEN" --env production

# ---- 9. Verify -------------------------------------------------------------------------------
log "Verifying"
sleep 15
API_STATUS="$(curl -sS -m 20 -o /dev/null -w "%{http_code}" "$API_URL/api/quotes" || echo "000")"
echo "  API status: $API_STATUS (401 is correct - auth is on)"

echo -e "\n\033[1;32mDone.\033[0m"
echo "Frontend: $SWA_URL"
echo "Backend:  $API_URL"
echo -e "\nTeardown: ./deploy.sh --down"
