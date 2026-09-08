# Day 24 / Piece 1 — Deployment Stacks + azd

Backend is [day-23/piece1](../../day-23/piece1)'s `QuotesApi`, copied unmodified into
[QuotesApi/](QuotesApi/). Frontend is day-23/piece1's Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/) - neither needed a code change for this exercise, same
as day-23. Everything new lives in [azure.yaml](azure.yaml) and [infra/](infra/).

**Scope note:** "the full stack" here means the same API + SQL + Service Bus stack day-23
defined - `azd` only provisions/deploys the `api` service. The frontend keeps deploying through
its existing day-17 Static Web App pipeline, untouched by this piece; adding it as a second azd
service (host: staticwebapp) would have been straightforward but was out of scope for what this
exercise is actually testing (Deployment Stacks + azd, not a second frontend hosting model).

## Current status

**`az stack sub validate` run live, successfully, for both `dev` and `prod`** - genuine,
read-only validation against the real subscription, standing in for `azd provision --preview`
which turned out not to be supported for this alpha feature (see section 4 - a real finding from
this session, not a shortcut). Both exited `0`.

**Nothing has been provisioned or deployed for real.** `azd provision` (dev or prod), `az stack
sub create`, and `azd deploy` have not been run - dev would create real billable resources
(`syquotes24dev-rg` and everything in it); prod would bring the live `syquotes17-rg`/
`syquotes19-rg` resources under Deployment Stacks management with `denyDelete` protection, a real
behavior change to production infrastructure. Both need an explicit go-ahead first - see
[infra/deploy.md](infra/deploy.md) for the exact commands that would apply either.

## 1. Layout

```
azure.yaml                   - azd project config: one service, "api" -> QuotesApi, host: appservice
infra/
  main.bicep                  - same shape as day-23's, plus two azd conventions (see section 3)
  main.parameters.json         - azd parameters file, values come from each azd environment's env vars
  modules/
    api.bicep                  - day-23's module + an `azd-service-name` tag on the site
    sql.bicep                  - unchanged from day-23
    servicebus.bicep           - unchanged from day-23
  deploy.md                    - exact commands for every step, including the ones not yet run
```

## 2. `azure.yaml`

```yaml
# yaml-language-server: $schema=https://raw.githubusercontent.com/Azure/azure-dev/main/schemas/v1.0/azure.yaml.json

name: thinkschool-quotesapi-day24
metadata:
  template: thinkschool-quotesapi-day24@0.1.0

# Only the backend is provisioned/deployed through azd in this piece - the
# frontend (quotes-list-detail/) is copied in unmodified from day-23/piece1
# purely as read-only reference (per this piece's scope: "the full stack"
# means the same API + SQL + Service Bus stack day-23 defined, now driven by
# azd + Deployment Stacks instead of `az deployment sub create`). The
# frontend still deploys via its existing day-17 Static Web App pipeline,
# untouched by this piece.
services:
  api:
    project: ./QuotesApi
    host: appservice
    language: dotnet
```

## 3. `infra/main.bicep`

Same resources and parameters as [day-23/piece1/infra/main.bicep](../../day-23/piece1/infra/main.bicep),
plus exactly two azd-specific additions (comment block at the top of the file explains why):
an `AZURE_RESOURCE_GROUP` output (so azd's `appservice` service target knows which resource group
to search) and an `azd-env-name` tag on both resource groups. `modules/api.bicep` gets one more:
an `azd-service-name: api` tag on the site itself, which is what `azd deploy` actually keys off to
find the right `Microsoft.Web/sites` resource.

```bicep
// Day 24 / Piece 1 infrastructure: the same API + Azure SQL + Service Bus
// stack as day-23/piece1, now provisioned through `azd provision` using
// Azure Deployment Stacks (alpha.deployment.stacks) instead of a plain
// `az deployment sub create` - see README "What Deployment Stacks give you"
// for the one-line answer, and "Current status" for what was actually run.
//
// Two differences from day-23/piece1/infra/main.bicep, both azd
// conventions, not behavior changes:
//   1. `AZURE_RESOURCE_GROUP` output - azd's `appservice` service target
//      resolves *which* resource group to search by reading this exact
//      output name, then finds the site inside it by its `azd-service-name`
//      tag (added in modules/api.bicep). Without this output, `azd deploy`
//      has nothing to look in.
//   2. `azd-env-name` tag on both resource groups - not required for azd to
//      function (unlike the output above), but it's what makes `az group
//      list --tag azd-env-name=<env>` and the Azure Portal's own azd
//      extension recognize these groups as azd-managed.
//
// Deploy at subscription scope so this template can create its own resource
// group(s) - same reasoning as day-23: dev gets one fresh resource group,
// prod's real Service Bus namespace still lives in a separate resource
// group from an earlier exercise (see serviceBusResourceGroupName).

targetScope = 'subscription'

@description('azd environment name (azd\'s AZURE_ENV_NAME) - "dev" or "prod" here. Used for tagging; every resource name comes from its own parameter so dev/prod can pick genuinely different values, not just a suffix.')
@allowed([
  'dev'
  'prod'
])
param environmentName string

@description('Resource group for the API and SQL resources. Created if it does not already exist.')
param resourceGroupName string

@description('Resource group for the Service Bus namespace. Created if it does not already exist. Defaults to resourceGroupName so a dev stack is one resource group unless told otherwise.')
param serviceBusResourceGroupName string = resourceGroupName

@description('Azure region for every resource in this deployment.')
param location string = 'eastasia'

@description('Object ID of the AAD principal that becomes the Azure SQL AAD admin. Defaults to azd\'s own AZURE_PRINCIPAL_ID (the signed-in user provisioning this environment).')
param sqlAadAdminObjectId string

@description('Display name of the AAD principal set as the SQL AAD admin.')
param sqlAadAdminName string

@description('Web App name.')
param apiAppName string

@description('App Service Plan name. Defaults to "<apiAppName>-plan" - override when codifying an existing plan whose name predates this convention.')
param apiServicePlanName string = '${apiAppName}-plan'

@description('App Service Plan SKU name.')
param apiSkuName string = 'B1'

@description('App Service Plan SKU tier matching apiSkuName.')
param apiSkuTier string = 'Basic'

@description('CORS allowed origin, wired in as the Cors__AllowedOrigin app setting.')
param corsAllowedOrigin string

@description('SQL logical server name (globally unique).')
param sqlServerName string

@description('SQL database name.')
param sqlDatabaseName string = 'quotesdb'

@description('SQL database SKU name.')
param sqlSkuName string = 'GP_S_Gen5'

@description('SQL database SKU tier matching sqlSkuName.')
param sqlSkuTier string = 'GeneralPurpose'

@description('SQL database SKU capacity (vCores for GeneralPurpose, DTUs for Basic).')
param sqlSkuCapacity int = 1

@description('Service Bus namespace name (globally unique).')
param serviceBusNamespaceName string

@description('Service Bus namespace SKU.')
param serviceBusSkuName string = 'Standard'

@description('Service Bus topic name.')
param serviceBusTopicName string = 'quote-events'

@description('azd service name the API site answers to - must match azure.yaml\'s services.api key.')
param apiAzdServiceName string = 'api'

var tags = {
  environment: environmentName
  project: 'thinkschool-quotesapi'
  managedBy: 'azd'
  'azd-env-name': environmentName
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

resource serviceBusRg 'Microsoft.Resources/resourceGroups@2024-03-01' = if (serviceBusResourceGroupName != resourceGroupName) {
  name: serviceBusResourceGroupName
  location: location
  tags: tags
}

module api 'modules/api.bicep' = {
  name: 'api'
  scope: rg
  params: {
    location: location
    appServicePlanName: apiServicePlanName
    apiAppName: apiAppName
    skuName: apiSkuName
    skuTier: apiSkuTier
    aspNetCoreEnvironment: environmentName == 'prod' ? 'Production' : 'Development'
    azdServiceName: apiAzdServiceName
    extraAppSettings: [
      {
        name: 'Cors__AllowedOrigin'
        value: corsAllowedOrigin
      }
    ]
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    location: location
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    aadAdminObjectId: sqlAadAdminObjectId
    aadAdminName: sqlAadAdminName
    skuName: sqlSkuName
    skuTier: sqlSkuTier
    skuCapacity: sqlSkuCapacity
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus'
  scope: resourceGroup(serviceBusResourceGroupName)
  params: {
    location: location
    namespaceName: serviceBusNamespaceName
    skuName: serviceBusSkuName
    topicName: serviceBusTopicName
    dataOwnerPrincipalId: api.outputs.principalId
  }
  dependsOn: [
    serviceBusRg
  ]
}

output AZURE_RESOURCE_GROUP string = rg.name
output apiAppName string = apiAppName
output apiHostname string = api.outputs.defaultHostname
output apiPrincipalId string = api.outputs.principalId
output sqlServerFqdn string = sql.outputs.fullyQualifiedDomainName
output serviceBusFullyQualifiedNamespace string = serviceBus.outputs.fullyQualifiedNamespace
output serviceBusTopicName string = serviceBus.outputs.topicName
```

`az bicep build --file infra/main.bicep` compiles clean (exit `0`).

## 4. azd config for both environments

Two local azd environments (`.azure/dev/`, `.azure/prod/` - both gitignored by azd's own
generated `.azure/.gitignore`, "not intended to be committed", which also happens to be the
right call here since these env files hold this student's own AAD email/object ID - kept local,
never in a committed file, same convention day-23 used with `readEnvironmentVariable(...)`).

**`azd env get-values` (dev):**

```
API_APP_NAME="syquotes24dev-api"
API_SERVICE_PLAN_NAME="syquotes24dev-api-plan"
API_SKU_NAME="F1"
API_SKU_TIER="Free"
AZURE_ENV_NAME="dev"
AZURE_LOCATION="eastasia"
CORS_ALLOWED_ORIGIN="http://localhost:4200"
RESOURCE_GROUP_NAME="syquotes24dev-rg"
SERVICEBUS_NAMESPACE_NAME="syquotes24devsb"
SERVICEBUS_RESOURCE_GROUP_NAME="syquotes24dev-rg"
SQL_AAD_ADMIN_NAME="<redacted - this student's own AAD UPN>"
SQL_SERVER_NAME="syquotes24dev-sql"
```

**`azd env get-values` (prod):**

```
API_APP_NAME="syquotes17-api"
API_SERVICE_PLAN_NAME="syquotes17-plan"
API_SKU_NAME="B1"
API_SKU_TIER="Basic"
AZURE_ENV_NAME="prod"
AZURE_LOCATION="eastasia"
CORS_ALLOWED_ORIGIN="https://black-desert-0fde3f100.7.azurestaticapps.net"
RESOURCE_GROUP_NAME="syquotes17-rg"
SERVICEBUS_NAMESPACE_NAME="syquotes19sb"
SERVICEBUS_RESOURCE_GROUP_NAME="syquotes19-rg"
SQL_AAD_ADMIN_NAME="<redacted - this student's own AAD UPN>"
SQL_SERVER_NAME="syquotes17-sql"
```

(`AZURE_PRINCIPAL_ID` isn't listed - azd resolves it automatically, at provision time, from
whichever identity is currently `az login`'d; it wasn't populated yet since provision hasn't
run.) Note prod points at the real, already-existing `syquotes17-rg`/`syquotes19-rg` (same
resources day-23's `what-if` validated); dev is a fresh, not-yet-created resource group on
Free/cheap SKUs - the same dev/prod split day-23 made, just expressed as azd environments instead
of `.bicepparam` files.

## 5. What azd's `--preview` couldn't do

```
$ azd provision --preview --environment dev

Previewing Azure resource changes (azd provision --preview)
This is a preview. No changes will be applied to your Azure resources.
...
  WARNING: Feature 'deployment.stacks' is in alpha stage.
Creating a deployment plan
Generating infrastructure preview

ERROR: deployment failed: error deploying infrastructure: preview not supported
```

Same result on `azd` 1.31.1 and after upgrading to the latest 1.33.0 mid-session - genuinely not
supported yet, not a version issue. Fell back to `az stack sub validate` directly (bypassing azd,
talking to the same `Microsoft.Resources/deploymentStacks` API azd's Bicep provider uses
underneath) - real, live, read-only validation against the actual subscription, the same
"exercise the tool for real without spending money or touching live resources" spirit as day-23's
`what-if`.

## 6. `az stack sub validate` - dev (fresh, nothing exists yet)

```
$ az stack sub validate --name day24dev --location eastasia \
    --deny-settings-mode none --action-on-unmanage detachAll \
    --template-file infra/main.bicep --parameters environmentName=dev ...

{
  "id": ".../providers/Microsoft.Resources/deploymentStacks/day24dev",
  "name": "day24dev",
  "properties": {
    "denySettings": { "mode": "None", ... },
    "validatedResources": [
      { "id": ".../resourceGroups/syquotes24dev-rg" },
      { "id": ".../resourceGroups/syquotes24dev-rg/providers/Microsoft.Resources/deployments/api" },
      { "id": ".../resourceGroups/syquotes24dev-rg/providers/Microsoft.Resources/deployments/servicebus" },
      { "id": ".../resourceGroups/syquotes24dev-rg/providers/Microsoft.Resources/deployments/sql" },
      { "id": ".../Microsoft.Sql/servers/syquotes24dev-sql" },
      { "id": ".../Microsoft.Sql/servers/syquotes24dev-sql/databases/quotesdb" },
      { "id": ".../Microsoft.Sql/servers/syquotes24dev-sql/firewallRules/AllowAzureServices" },
      { "id": ".../Microsoft.Web/serverfarms/syquotes24dev-api-plan" },
      { "id": ".../Microsoft.Web/sites/syquotes24dev-api" }
    ]
  },
  "type": "Microsoft.Resources/deploymentStacks"
}
```

Exit code `0`. Nine resources this stack would manage as one unit if created for real - the
resource group, the three nested module deployments, and the six leaf resources under them
(Service Bus's own leaf resources are one level deeper, inside the `servicebus` nested
deployment at a different resource-group scope, so they don't get their own top-level entry here
- the same nesting `what-if` showed in day-23).

## 7. `az stack sub validate` - prod (against the real, already-existing infrastructure)

```
$ az stack sub validate --name day24prod --location eastasia \
    --deny-settings-mode denyDelete --action-on-unmanage detachAll \
    --template-file infra/main.bicep --parameters environmentName=prod ...

{
  "id": ".../providers/Microsoft.Resources/deploymentStacks/day24prod",
  "name": "day24prod",
  "properties": {
    "denySettings": { "mode": "DenyDelete", ... },
    "validatedResources": [
      { "id": ".../resourceGroups/syquotes17-rg" },
      { "id": ".../resourceGroups/syquotes17-rg/providers/Microsoft.Resources/deployments/api" },
      { "id": ".../resourceGroups/syquotes17-rg/providers/Microsoft.Resources/deployments/sql" },
      { "id": ".../Microsoft.Sql/servers/syquotes17-sql" },
      { "id": ".../Microsoft.Sql/servers/syquotes17-sql/databases/quotesdb" },
      { "id": ".../Microsoft.Sql/servers/syquotes17-sql/firewallRules/AllowAzureServices" },
      { "id": ".../Microsoft.Web/serverfarms/syquotes17-plan" },
      { "id": ".../Microsoft.Web/sites/syquotes17-api" },
      { "id": ".../resourceGroups/syquotes19-rg" },
      { "id": ".../resourceGroups/syquotes19-rg/providers/Microsoft.Resources/deployments/servicebus" }
    ]
  },
  "type": "Microsoft.Resources/deploymentStacks"
}
```

Exit code `0`. This is the "promote to prod" half of the exercise: the exact same template, same
module wiring, validated against `syquotes17-rg`'s and `syquotes19-rg`'s *real, already-running*
resources (the ones day-23's `what-if` proved already match this Bicep) - confirming they could be
adopted into a Deployment Stack, with `denyDelete` protection, without ARM rejecting anything.
`deny-settings-mode denyDelete` here (vs. dev's `none`) is deliberate: a real environment should
actually deny out-of-band deletes; a throwaway dev stack shouldn't get in its own way.

## What Deployment Stacks give you over plain deployments

One line: a plain `az deployment ... create` deploys whatever's in the template and then forgets
it ever ran - there's no record anywhere of "these N resources belong together" - so teardown
means manually tracking down and deleting every resource yourself, and nothing stops someone
changing a managed resource by hand afterward; a Deployment Stack turns that same template into
one manageable object (`Microsoft.Resources/deploymentStacks/day24dev`) that owns its resources as
a set, so `azd down` / `az stack sub delete` removes exactly what the stack created and nothing
else, and `denySettings` (used here for prod) can block out-of-band portal edits to anything the
stack manages - turning "no portal click-ops" from a house rule into something ARM itself enforces.

## What did I learn this session?

1. **An alpha feature can be alpha in more than one dimension at once.** `deployment.stacks` being
   "alpha" in azd didn't just mean rough edges - a whole documented flag (`--preview`) simply
   doesn't work with it yet, on the latest azd release, confirmed by upgrading mid-session
   specifically to rule out "this is just an old CLI." The right response wasn't to force it or
   fake the output, but to drop to the layer underneath (`az stack sub validate`, which azd's own
   Bicep provider calls) and get a real result there instead.
2. **Deployment Stacks nest resource-group-scoped module deployments as opaque entries, same as
   `what-if` did in day-23.** `validatedResources` lists `.../deployments/servicebus` as one
   line, not the namespace/topic/subscriptions/role-assignment inside it - the stack's view of
   "what it manages" is shaped by deployment scope the same way `what-if`'s diff output was. Two
   different Azure features, same underlying nesting quirk.
3. **`denySettings` is a per-deployment choice, not a stack-wide default worth hardcoding.** Using
   `none` for dev and `denyDelete` for prod in the same template, via different CLI flags rather
   than a Bicep parameter, made it obvious that "how protected should this environment be" is an
   operational decision made at deploy time, not something the template itself should bake in.

## What would break this

- **`azd deploy` has never actually been exercised**, so the `azd-service-name: api` tag's
  correctness is inferred from reading azd's docs/source behavior, not proven by a real deploy
  finding the site. If the tag value, the resource-group resolution via `AZURE_RESOURCE_GROUP`,
  or the site's `kind` (`app,linux`) don't line up with what azd's `appservice` target actually
  expects, the failure would only surface at real `azd deploy` time, not at `bicep build` or
  `stack validate` time - neither of those checks azd's own service-resolution logic at all.
- **Promoting prod into a Deployment Stack is a one-way-feeling operation this session didn't
  reverse.** Section 7's validate is a dry run, but the real `az stack sub create` against
  `syquotes17-rg` would be the *first* time these resources are stack-managed - if `denyDelete`
  turns out to be too aggressive (blocking a legitimate future manual fix), someone has to know to
  update or delete the stack resource itself (not just the underlying resources) to loosen it.
  That operational knowledge doesn't yet exist anywhere in this repo outside this README.
- **Two resource groups under one azd environment (`prod`) is unusual for azd's own conventions**,
  which generally assume one environment = one resource group (hence `AZURE_RESOURCE_GROUP` being
  singular). It works here because Service Bus's resource group is addressed directly in Bicep
  (`resourceGroup(serviceBusResourceGroupName)`) rather than through any azd-level multi-RG
  feature - but `azd`'s own tooling (e.g. `azd monitor`, resource listing in `azd show`) may only
  ever "see" `syquotes17-rg`, not `syquotes19-rg`, since azd's resource discovery follows that one
  output.
- **`sqlSkuCapacity` and a few other numeric/default-only parameters were deliberately left out of
  `main.parameters.json`** to sidestep ARM parameter-file type coercion between azd's string
  `${VAR}` substitution and Bicep's `int` type (untested here whether azd would have handled it
  correctly) - both environments happen to want the same value (`1`) today, so this was never
  actually forced to work. A future environment needing a different SQL capacity would need that
  parameter added back and the type-coercion question actually resolved, not sidestepped again.

## Notes for mentor

- `day-23/piece1` (and everything upstream of it) was read-only reference / copy source - nothing
  there was modified. Backend and frontend were copied into `day-24/piece1` unchanged; this
  piece's only new content is `azure.yaml`, `infra/`, and this README.
- Both `az stack sub validate` runs above are real, live output against this student's actual
  "Azure for Students" subscription (`30e5e569-...`) - not simulated or hand-edited. Neither
  `azd provision` nor `az stack sub create` was run - see "Current status" for why, and
  `infra/deploy.md` for the exact commands that would apply either once given the go-ahead.
- Section 5 (the `--preview` failure) is worth a second look - it's a real, reproducible gap in
  azd's current alpha support for Deployment Stacks, not something this session worked around by
  choice.

## GitHub link

Not pushed yet - link to follow once pushed to the `thinkbridge-thinkschool` org, per this user's
standing preference that git actions (including read-only ones) need explicit permission each
time.
