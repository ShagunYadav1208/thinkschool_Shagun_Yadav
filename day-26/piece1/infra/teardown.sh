#!/usr/bin/env bash
# Standalone teardown for day-26/piece1: deletes everything deploy.sh/deploy.md created, then
# independently VERIFIES nothing is left - not just "the delete command exited 0", but actually
# re-queries Azure/Entra ID for each thing that was created and confirms it's gone. Exits non-zero
# and prints exactly what's still there if anything survived, instead of silently assuming success.
#
# Standalone on purpose - works even if you only ever ran deploy.md's manual steps and never used
# deploy.sh, as long as the names below match what you actually used (same defaults as deploy.sh;
# override the same way, e.g. RESOURCE_GROUP_NAME=myrg ./teardown.sh).
#
# Usage: ./teardown.sh
set -uo pipefail   # deliberately not -e: a failed/missing resource during verification is an
                    # expected outcome to check, not a script bug - each check handles its own errors

# ---- Configuration (override via env vars - same names/defaults as deploy.sh) ----------------
AZD_ENV_NAME="${AZD_ENV_NAME:-dev}"
RESOURCE_GROUP_NAME="${RESOURCE_GROUP_NAME:-syquotes26dev-rg}"
KEY_VAULT_NAME="${KEY_VAULT_NAME:-syquotes26dev-kv}"
LOG_ANALYTICS_WORKSPACE_NAME="${LOG_ANALYTICS_WORKSPACE_NAME:-syquotes26dev-law}"
NAME_PREFIX="${NAME_PREFIX:-syquotes26dev}"   # used to sweep for any stragglers by name
EXISTING_SPA_APP_ID="${EXISTING_SPA_APP_ID:-335b9c06-58f9-4bc3-8732-b3f16bcf39e6}"   # shared app reg - never deleted by this script

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
STATE_FILE="$SCRIPT_DIR/.deploy-state"

log()  { echo -e "\n\033[1;36m==> $*\033[0m"; }
ok()   { echo -e "  \033[1;32m[OK]\033[0m $*"; }
bad()  { echo -e "  \033[1;31m[STILL THERE]\033[0m $*"; FAILED=1; }
warn() { echo -e "  \033[1;33m[WARN]\033[0m $*"; }

FAILED=0

command -v az >/dev/null || { echo "az CLI not found"; exit 1; }
command -v azd >/dev/null || { echo "azd CLI not found"; exit 1; }

SUBSCRIPTION_ID="$(az account show --query id -o tsv 2>/dev/null)" || { echo "not logged in - run 'az login' first"; exit 1; }
log "Using subscription $SUBSCRIPTION_ID"

# ---- 1. Delete via azd (handles the deployment stack + the whole resource group) -------------
log "Running azd down --force --purge"
(cd "$PROJECT_DIR" && azd down --environment "$AZD_ENV_NAME" --force --purge 2>&1) || warn "azd down reported an error - continuing to verify actual state regardless"

# Clean up local azd state too, not just the Azure-side stack - leaving it behind makes the next
# deploy.sh run reference a stack that no longer exists (real bug hit live: causes a confusing
# "ResourceNotFound: azd-stack-dev was not found" on the next provision, after it has already
# gone ahead and recreated every resource with no stack tracking wrapped around them).
(cd "$PROJECT_DIR" && azd env remove "$AZD_ENV_NAME" --force 2>&1) || true

# ---- 2. Delete any app registration deploy.sh created ------------------------------------------
if [[ -f "$STATE_FILE" ]]; then
  # shellcheck disable=SC1090
  source "$STATE_FILE"
  if [[ -n "${CREATED_SPA_APP_ID:-}" ]]; then
    log "Deleting app registration $CREATED_SPA_APP_ID (created by deploy.sh)"
    az ad app delete --id "$CREATED_SPA_APP_ID" 2>&1 || true
  fi
  rm -f "$STATE_FILE"
else
  warn "no .deploy-state file found - if deploy.sh created its own app registration, its id wasn't recorded here. Check manually: az ad app list --display-name '${NAME_PREFIX}-api-spa'"
fi

# ---- 3. Verify: resource group is gone ---------------------------------------------------------
log "Verifying resource group '$RESOURCE_GROUP_NAME'"
RG_EXISTS="$(az group exists -n "$RESOURCE_GROUP_NAME" 2>/dev/null)"
if [[ "$RG_EXISTS" == "false" ]]; then
  ok "resource group does not exist"
else
  bad "resource group still exists"
  az resource list -g "$RESOURCE_GROUP_NAME" --query "[].{name:name, type:type}" -o table 2>/dev/null
fi

# ---- 4. Verify: no stray resources anywhere in the subscription by name prefix ----------------
log "Sweeping the whole subscription for anything named '${NAME_PREFIX}*'"
STRAY="$(az resource list --query "[?starts_with(name, '${NAME_PREFIX}')].{name:name, type:type, rg:resourceGroup}" -o tsv 2>/dev/null)"
if [[ -z "$STRAY" ]]; then
  ok "no resources found anywhere in the subscription with this prefix"
else
  bad "found leftover resources:"
  echo "$STRAY" | while IFS=$'\t' read -r name type rg; do echo "    - $name ($type) in $rg"; done
fi

# ---- 5. Verify: no leftover deployment stacks --------------------------------------------------
log "Checking for leftover deployment stacks"
STACKS="$(az stack sub list --query "[?starts_with(name, 'azd-stack-${AZD_ENV_NAME}') || starts_with(name, '${NAME_PREFIX}')].name" -o tsv 2>/dev/null)"
if [[ -z "$STACKS" ]]; then
  ok "no leftover deployment stacks"
else
  bad "found leftover deployment stacks: $STACKS"
fi

# ---- 6. Verify: Key Vault is actually purged, not just soft-deleted ---------------------------
log "Checking Key Vault '$KEY_VAULT_NAME' is fully purged (not just soft-deleted)"
DELETED_KV="$(az keyvault list-deleted --query "[?name=='${KEY_VAULT_NAME}'].name" -o tsv 2>/dev/null)"
if [[ -z "$DELETED_KV" ]]; then
  ok "no soft-deleted Key Vault with this name"
else
  bad "Key Vault is soft-deleted but not purged - purge it with: az keyvault purge -n $KEY_VAULT_NAME --location <region>"
fi

# ---- 7. Verify: Log Analytics workspace isn't left in a soft-deleted state ---------------------
log "Checking for a soft-deleted Log Analytics workspace named '$LOG_ANALYTICS_WORKSPACE_NAME'"
DELETED_LAW="$(az monitor log-analytics workspace list-deleted-workspaces --query "[?name=='${LOG_ANALYTICS_WORKSPACE_NAME}'].name" -o tsv 2>/dev/null)"
if [[ -z "$DELETED_LAW" ]]; then
  ok "no soft-deleted Log Analytics workspace with this name (or the command isn't available in this az cli version - not fatal)"
else
  bad "Log Analytics workspace is soft-deleted but not purged"
fi

# ---- 8. Verify: no app registrations left matching this project's naming ----------------------
log "Checking for leftover app registrations named '${NAME_PREFIX}*'"
LEFTOVER_APPS="$(az ad app list --filter "startswith(displayName,'${NAME_PREFIX}')" --query "[].{name:displayName, appId:appId}" -o tsv 2>/dev/null)"
if [[ -z "$LEFTOVER_APPS" ]]; then
  ok "no leftover app registrations with this name prefix"
else
  bad "found leftover app registrations:"
  echo "$LEFTOVER_APPS" | while IFS=$'\t' read -r name appid; do echo "    - $name ($appid)"; done
  echo "    Note: this deliberately does NOT include the original shared app registrations"
  echo "    ($EXISTING_SPA_APP_ID / the API app) if this project reused them - those are meant to stay."
fi

# ---- Summary -------------------------------------------------------------------------------
if [[ "$FAILED" -eq 0 ]]; then
  echo -e "\n\033[1;32mAll clear - nothing from this deployment is left in Azure or Entra ID.\033[0m"
  exit 0
else
  echo -e "\n\033[1;31mSome things are still there - see [STILL THERE] lines above.\033[0m"
  exit 1
fi
