#!/usr/bin/env bash
# Optional next step, not required by this exercise's deliverable (resource group + environment +
# `az containerapp env show`) — included because the exercise's own text walks through the
# az containerapp create flags, and day-5/piece2 already produced a real image to point this at.
#
# Not run in this session (no subscription available — see ../README.md). Flags below were checked
# against `az containerapp create --help` on Azure CLI 2.89.0, not copied from the exercise text
# verbatim, since a couple of names have changed (see README, "one correction to the exercise text").
#
# Assumes the day-5/piece2 image has been pushed somewhere `az containerapp create` can pull from
# (Azure Container Registry, Docker Hub, etc.) — a purely local `docker images` image built by
# `dotnet publish /t:PublishContainer` is not reachable from Azure until it's pushed to a registry.

set -euo pipefail

RESOURCE_GROUP="thinkschool-rg"
ENVIRONMENT="thinkschool-env"
APP_NAME="quotes-api"
IMAGE="<your-registry>/quotes-api:0.1.0"   # push day-5/piece2's image here first

az containerapp create \
  --name "$APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --environment "$ENVIRONMENT" \
  --image "$IMAGE" \
  --target-port 8080 \
  --ingress external \
  --scale-rule-name http-scale \
  --scale-rule-type http \
  --scale-rule-http-concurrency 50 \
  --min-replicas 0 \
  --max-replicas 3

echo "==> Fetching the public URL"
az containerapp show \
  --name "$APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query properties.configuration.ingress.fqdn \
  --output tsv
