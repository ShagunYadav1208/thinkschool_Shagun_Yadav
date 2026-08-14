#!/usr/bin/env bash
# Day 5 Piece 3 — Azure Container Apps fundamentals.
#
# Creates the resource group + Container Apps environment this exercise asks for, then dumps the
# real `az containerapp env show` JSON to env-show-output.json next to this script.
#
# Prerequisites (nothing to hardcode — auth is interactive, not a token in this script):
#   1. az login                       (interactive browser or device-code sign-in)
#   2. az account set --subscription "<name-or-id>"   (if the account has more than one)
#   3. Azure CLI >= 2.60 (this was written/verified against 2.89.0)
#
# No API key, connection string, or token belongs in this file or anywhere in source control —
# `az login` puts a session token in the CLI's own local token cache, not in this script. If you
# find yourself wanting to paste a secret here to make this "runnable," that's the sign it
# shouldn't be a script at all — use `az login` and re-run as-is.

set -euo pipefail

RESOURCE_GROUP="thinkschool-rg"
LOCATION="centralindia"
ENVIRONMENT="thinkschool-env"

echo "==> Whoami / subscription check"
az account show --output table

echo "==> Registering required resource providers (one-time per subscription)"
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait

echo "==> Creating resource group: $RESOURCE_GROUP ($LOCATION)"
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION"

echo "==> Creating Container Apps environment: $ENVIRONMENT"
az containerapp env create \
  --name "$ENVIRONMENT" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION"

echo "==> Fetching environment details as JSON"
az containerapp env show \
  --name "$ENVIRONMENT" \
  --resource-group "$RESOURCE_GROUP" \
  --output json | tee "$(dirname "$0")/env-show-output.json"

echo "==> Done. Real output saved to scripts/env-show-output.json"
