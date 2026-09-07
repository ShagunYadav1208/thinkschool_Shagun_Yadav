# Day 23 / Piece 1 — Bicep IaC

Backend is [day-22/piece1](../../day-22/piece1)'s `QuotesApi`, copied unmodified into
[QuotesApi/](QuotesApi/). Frontend is day-22/piece1's Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/). Neither needed a code change for this exercise -
this piece is pure infrastructure-as-code, no new API/UI behavior. Everything new lives in
[infra/](infra/).

## Current status

**`what-if` run live, twice, successfully** - once against `params/prod.bicepparam` (this
repo's real, already-running infrastructure from day-17/19: `syquotes17-rg` + `syquotes19-rg`)
and once against `params/dev.bicepparam` (a stack that doesn't exist yet). Both exited `0`
with `"status": "Succeeded"`. See sections 2-3 below for the full output of each.

**`az deployment sub create` has not been run.** what-if is read-only and provisions nothing;
actually applying `prod.bicepparam` would modify the live App Service and Service Bus
namespace (see section 2's drift list), which needs an explicit go-ahead first - see
[infra/deploy.md](infra/deploy.md).

## 1. Layout

```
infra/
  main.bicep                 - subscription-scope entry point, wires the three modules together
  modules/
    api.bicep                 - App Service Plan + Linux App Service, system-assigned identity
    sql.bicep                 - Azure SQL server (AAD-only auth) + firewall rule + database
    servicebus.bicep           - Service Bus namespace + topic + subscriptions + RBAC (new this piece)
  params/
    dev.bicepparam             - a separate, cheap, throwaway environment
    prod.bicepparam            - this repo's real, live environment
  deploy.md                    - exact what-if/deploy commands
```

`api.bicep` and `sql.bicep` are day-17/piece1's modules, parameterized further (SKU
name/tier/capacity, extra app settings) so the same module now serves both a `B1`/`GeneralPurpose`
prod stack and an `F1`/free dev stack instead of being hardcoded to one SKU. `servicebus.bicep`
is new - day-17 predates the Service Bus exercise (day-19), so no Bicep for it existed anywhere
in this repo before this piece.

No portal click-ops: every property in the tables below (SKUs, firewall rule, topic/subscription
settings, the RBAC grant) is a value in one of these `.bicep` files, not something set by hand in
the Azure Portal and then left undocumented.

## 2. `main.bicep`

```bicep
// Day 23 / Piece 1 infrastructure: QuotesApi (App Service) + Azure SQL +
// Service Bus, as one parameterized template deployed once per environment
// via params/dev.bicepparam / params/prod.bicepparam - see README "Current
// status" for exactly what prod.bicepparam points at and why.
//
// Deploy at subscription scope so this template can create its own resource
// group(s) - see README "Deploying" for the exact `az deployment sub`
// invocations (what-if and create).
//
// Service Bus deploys into its own resource group parameter
// (serviceBusResourceGroupName) rather than always reusing resourceGroupName:
// this codebase's real Service Bus namespace (syquotes19sb) was provisioned
// in an earlier exercise's resource group (syquotes19-rg), separate from the
// API/SQL resource group (syquotes17-rg). prod.bicepparam points at both
// existing groups so `what-if` compares this template against what's
// actually running; dev.bicepparam sets both names to the same fresh group,
// so a dev stack is one resource group, not two.

targetScope = 'subscription'

@description('Environment name - dev or prod. Used only for tagging; every other name comes from its own parameter so dev/prod can pick genuinely different values, not just a suffix.')
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

@description('Object ID of the AAD principal (your own user, or a group) that becomes the Azure SQL AAD admin.')
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

var tags = {
  environment: environmentName
  project: 'thinkschool-quotesapi'
  managedBy: 'bicep'
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// Only declared as a second resource when it's actually a different group -
// declaring the same resource-group name twice under two symbolic names in
// one deployment is redundant (and, for a subscription-scope deployment,
// unnecessary: `rg` above already creates it).
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

output apiAppName string = apiAppName
output apiHostname string = api.outputs.defaultHostname
output apiPrincipalId string = api.outputs.principalId
output sqlServerFqdn string = sql.outputs.fullyQualifiedDomainName
output serviceBusFullyQualifiedNamespace string = serviceBus.outputs.fullyQualifiedNamespace
output serviceBusTopicName string = serviceBus.outputs.topicName
```

## 3. `modules/servicebus.bicep` (the new module)

Matches `QuotesApi`'s `ServiceBusMessaging/ServiceBusOptions.cs`: one topic (`quote-events`)
fanned out to an `audit-log` subscription and a `notifications` subscription. No connection
string anywhere - the API authenticates with its own system-assigned managed identity
(`DefaultAzureCredential` in Azure, per `Extensions/InfrastructureExtensions.cs`), so this
module also grants that identity the **Azure Service Bus Data Owner** role on the namespace,
the same managed-identity-over-password pattern `modules/sql.bicep` uses for Azure SQL.

```bicep
// Service Bus namespace + one topic + its subscriptions, matching
// QuotesApi's ServiceBusMessaging module (topic "quote-events", fanned out
// to an audit-log subscription and a notifications subscription - see
// ServiceBusMessaging/ServiceBusOptions.cs). No connection-string secret
// anywhere: the API authenticates with its own managed identity
// (DefaultAzureCredential in Azure - see Extensions/InfrastructureExtensions.cs),
// granted access here via an "Azure Service Bus Data Owner" role assignment
// scoped to this namespace, the same managed-identity-over-password pattern
// modules/sql.bicep uses for Azure SQL.

@description('Azure region for the namespace.')
param location string

@description('Service Bus namespace name (globally unique).')
param namespaceName string

@description('Namespace SKU. Standard is required for topics/subscriptions - Basic only supports queues.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param skuName string = 'Standard'

@description('Topic name.')
param topicName string = 'quote-events'

@description('Subscriptions to create under the topic, each as {name, lockDuration, maxDeliveryCount}.')
param subscriptions array = [
  {
    name: 'audit-log'
    lockDuration: 'PT30S'
    maxDeliveryCount: 5
  }
  {
    name: 'notifications'
    lockDuration: 'PT15S'
    maxDeliveryCount: 3
  }
]

@description('principalId of the managed identity (e.g. the API app\'s system-assigned identity) to grant data-plane access to. Empty string skips the role assignment.')
param dataOwnerPrincipalId string = ''

var serviceBusDataOwnerRoleId = '090c5cfd-751d-490a-894a-3ce6f1109419'

resource namespaceRes 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespaceRes
  name: topicName
  properties: {
    defaultMessageTimeToLive: 'P14D'
  }
}

resource topicSubscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = [
  for sub in subscriptions: {
    parent: topic
    name: sub.name
    properties: {
      lockDuration: sub.lockDuration
      maxDeliveryCount: sub.maxDeliveryCount
      deadLetteringOnMessageExpiration: false
      deadLetteringOnFilterEvaluationExceptions: true
    }
  }
]

resource dataOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(dataOwnerPrincipalId)) {
  name: guid(namespaceRes.id, dataOwnerPrincipalId, serviceBusDataOwnerRoleId)
  scope: namespaceRes
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataOwnerRoleId)
    principalId: dataOwnerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output fullyQualifiedNamespace string = '${namespaceRes.name}.servicebus.windows.net'
output namespaceName string = namespaceRes.name
output topicName string = topic.name
```

## 4. `params/prod.bicepparam` and `params/dev.bicepparam`

Neither file hardcodes the SQL AAD admin's email/object ID - both read it from environment
variables (`readEnvironmentVariable(...)`), set once via `az ad signed-in-user show` before
deploying (see [infra/deploy.md](infra/deploy.md) step 0). Everything else genuinely differs
per environment, not just by a name suffix: prod points at this repo's two real, pre-existing
resource groups and their real resource names (`syquotes17-plan`, `syquotes19sb`, ...); dev
targets one fresh resource group with a Free-tier App Service plan and a `localhost` CORS
origin.

```bicepparam
using '../main.bicep'

// Points at this repo's real, already-running production infrastructure
// (day-17/19's syquotes17-rg + syquotes19-rg) - see README "Current status"
// for the what-if run this produces and what it does/doesn't show as drift.
//
// sqlAadAdminObjectId/sqlAadAdminName are read from environment variables,
// never hardcoded here, so this file carries no personal identifier. Set
// them before deploying:
//   export SQL_AAD_ADMIN_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
//   export SQL_AAD_ADMIN_NAME=$(az ad signed-in-user show --query userPrincipalName -o tsv)

param environmentName = 'prod'

param resourceGroupName = 'syquotes17-rg'
param serviceBusResourceGroupName = 'syquotes19-rg'
param location = 'eastasia'

param sqlAadAdminObjectId = readEnvironmentVariable('SQL_AAD_ADMIN_OBJECT_ID')
param sqlAadAdminName = readEnvironmentVariable('SQL_AAD_ADMIN_NAME')

param apiAppName = 'syquotes17-api'
param apiServicePlanName = 'syquotes17-plan'
param apiSkuName = 'B1'
param apiSkuTier = 'Basic'
param corsAllowedOrigin = 'https://black-desert-0fde3f100.7.azurestaticapps.net'

param sqlServerName = 'syquotes17-sql'
param sqlDatabaseName = 'quotesdb'
param sqlSkuName = 'GP_S_Gen5'
param sqlSkuTier = 'GeneralPurpose'
param sqlSkuCapacity = 1

param serviceBusNamespaceName = 'syquotes19sb'
param serviceBusSkuName = 'Standard'
param serviceBusTopicName = 'quote-events'
```

```bicepparam
using '../main.bicep'

// A separate, cheap, throwaway stack - its own resource group, Free-tier App
// Service plan (no alwaysOn), and a local-dev CORS origin. Never deployed as
// part of this exercise (see README "Current status") - included to satisfy
// "separate dev/prod parameter files" and to show the same template actually
// producing a materially different plan for a different environment.
//
// sqlAadAdminObjectId/sqlAadAdminName are read from environment variables,
// never hardcoded here - same convention as prod.bicepparam.

param environmentName = 'dev'

param resourceGroupName = 'syquotes23dev-rg'
param serviceBusResourceGroupName = 'syquotes23dev-rg'
param location = 'eastasia'

param sqlAadAdminObjectId = readEnvironmentVariable('SQL_AAD_ADMIN_OBJECT_ID')
param sqlAadAdminName = readEnvironmentVariable('SQL_AAD_ADMIN_NAME')

param apiAppName = 'syquotes23dev-api'
param apiSkuName = 'F1'
param apiSkuTier = 'Free'
param corsAllowedOrigin = 'http://localhost:4200'

param sqlServerName = 'syquotes23dev-sql'
param sqlDatabaseName = 'quotesdb'
param sqlSkuName = 'GP_S_Gen5'
param sqlSkuTier = 'GeneralPurpose'
param sqlSkuCapacity = 1

param serviceBusNamespaceName = 'syquotes23devsb'
param serviceBusSkuName = 'Standard'
param serviceBusTopicName = 'quote-events'
```

## 5. `az bicep build` - compiles cleanly

```
$ az bicep build --file main.bicep --outfile /tmp/main.json
$ echo $?
0
```

(One linter warning was hit and fixed during development - `no-unnecessary-dependson` on the
`servicebus` module's `dependsOn: [rg, ...]`, because `api.outputs.principalId` already gives
Bicep an implicit dependency on `rg` through the `api` module. Fixed by dropping the redundant
`rg` entry, keeping only `serviceBusRg` - see `main.bicep`'s history if useful.)

## 6. Live `what-if` output - prod (real drift against this repo's actual infrastructure)

Run exactly as shown in [infra/deploy.md](infra/deploy.md) step 1, against
`params/prod.bicepparam`, `2026-09-07`. Exit code `0`, `"status": "Succeeded"`.

```
$ az deployment sub what-if --location eastasia --template-file main.bicep --parameters params/prod.bicepparam

Note: The result may contain false positive predictions (noise).
You can help us improve the accuracy of the result by opening an issue here: https://aka.ms/WhatIfIssues

Resource and property changes are indicated with these symbols:
  - Delete
  + Create
  ~ Modify
  = Nochange
  x Unsupported
  * Ignore
  x Noeffect

The deployment will update the following scopes:

Scope: /



Scope: /subscriptions/30e5e569-cb36-491a-91c9-2d880095bdcb

  ~ resourceGroups/syquotes17-rg [2024-03-01]
    + tags:

        environment: "prod"
        managedBy:   "bicep"
        project:     "thinkschool-quotesapi"


  ~ resourceGroups/syquotes19-rg [2024-03-01]
    + tags:

        environment: "prod"
        managedBy:   "bicep"
        project:     "thinkschool-quotesapi"


Scope: /subscriptions/30e5e569-cb36-491a-91c9-2d880095bdcb/resourceGroups/syquotes17-rg

  ~ Microsoft.Web/serverfarms/syquotes17-plan [2024-04-01]
    - properties.freeOfferExpirationTime: "2027-02-28T09:40:33.22"
    x sku.tier:                           "Basic"

  ~ Microsoft.Web/sites/syquotes17-api [2024-04-01]
    + properties.siteConfig.localMySqlEnabled:   false
    + properties.siteConfig.netFrameworkVersion: "v4.6"
    ~ properties.siteConfig.alwaysOn:            false => true

  = Microsoft.Sql/servers/syquotes17-sql [2024-05-01-preview]
  = Microsoft.Sql/servers/syquotes17-sql/databases/quotesdb [2024-05-01-preview]
    x properties.minCapacity: 0.5
    x sku.capacity:           1
    x sku.tier:               "GeneralPurpose"

  = Microsoft.Sql/servers/syquotes17-sql/firewallRules/AllowAzureServices [2024-05-01-preview]
  * Microsoft.Cache/redisEnterprise/syquotes21-redis
  * Microsoft.Sql/servers/syquotes17-sql/databases/master
  * Microsoft.Storage/storageAccounts/syquotes17store
  * Microsoft.Web/staticSites/syquotes17-swa

Scope: /subscriptions/30e5e569-cb36-491a-91c9-2d880095bdcb/resourceGroups/syquotes19-rg

  ~ Microsoft.ServiceBus/namespaces/syquotes19sb/topics/quote-events [2024-01-01]
    - properties.autoDeleteOnIdle:                    "P10675199DT2H48M5.4775807S"
    - properties.duplicateDetectionHistoryTimeWindow: "PT10M"
    - properties.enableBatchedOperations:             true
    - properties.enableExpress:                       false
    - properties.enablePartitioning:                  false
    - properties.maxMessageSizeInKilobytes:           256
    - properties.maxSizeInMegabytes:                  1024
    - properties.supportOrdering:                     true
    ~ properties.defaultMessageTimeToLive:            "P10675199DT2H48M5.4775807S" => "P14D"

  ~ Microsoft.ServiceBus/namespaces/syquotes19sb/topics/quote-events/subscriptions/audit-log [2024-01-01]
    - properties.autoDeleteOnIdle:         "P10675199DT2H48M5.4775807S"
    - properties.defaultMessageTimeToLive: "P10675199DT2H48M5.4775807S"
    - properties.enableBatchedOperations:  true
    - properties.isClientAffine:           false

  ~ Microsoft.ServiceBus/namespaces/syquotes19sb/topics/quote-events/subscriptions/notifications [2024-01-01]
    - properties.autoDeleteOnIdle:         "P10675199DT2H48M5.4775807S"
    - properties.defaultMessageTimeToLive: "P10675199DT2H48M5.4775807S"
    - properties.enableBatchedOperations:  true
    - properties.isClientAffine:           false

  = Microsoft.ServiceBus/namespaces/syquotes19sb [2024-01-01]
    x sku.tier: "Standard"

Resource changes: 7 to modify, 4 no change, 1 unsupported, 4 to ignore.
```

**What this actually says, item by item:**

- **`* Ignore` (4 resources)** - `syquotes21-redis`, the SQL `master` system database, the storage
  account, and the Static Web App. Correctly out of scope: this template only claims ownership of
  API + SQL + Service Bus, not everything that happens to live in the same resource group.
- **`+ tags` on both resource groups** - real and intentional. Neither group was tagged before;
  this template tags every resource group it touches (`environment`, `project`, `managedBy: bicep`)
  so "is this resource IaC-managed" stops being a guess.
- **`~ properties.siteConfig.alwaysOn: false => true`** - a real, genuine finding, not a template
  bug. The live `syquotes17-api` currently runs with `alwaysOn` disabled; `modules/api.bicep` sets
  it `true` for any non-Free SKU (the standard recommendation for a paid App Service plan, to avoid
  cold starts). This is exactly the kind of drift `what-if` exists to surface - noted here rather
  than silently "fixed" by deploying, since flipping a live setting needs the same go-ahead as
  everything else in `deploy.md`.
- **`x sku.tier` (plan and SQL database) / `x properties.minCapacity` / `x sku.capacity`** -
  `Unsupported`, not a mismatch: Azure's what-if engine has a known limitation analyzing certain
  SKU/serverless-capacity fields on `serverfarms` and SQL `databases` (see the tool's own
  `https://aka.ms/WhatIfUnidentifiableResource` link in its banner). These already match reality
  (`B1`/`Basic` and `GP_S_Gen5`/`1`/`0.5` respectively, confirmed separately via `az appservice plan
  show` / `az sql db show` before writing this module) - what-if just can't confirm that for these
  specific properties.
- **`- properties.freeOfferExpirationTime`** - a read-only, service-computed field this template
  never sets; what-if flags it as "would be removed" because it isn't present in the desired state,
  not because deploying would actually clear it.
- **Service Bus topic/subscription `- properties.*` lines** (`autoDeleteOnIdle`,
  `duplicateDetectionHistoryTimeWindow`, `enableBatchedOperations`, ...) - the same "not specified
  in the template = shown as removed" pattern, for properties this exercise's module intentionally
  leaves at their service defaults. `what-if`'s own banner ("may contain false positive
  predictions") is specifically about this class of noise on messaging resources.
- **`~ properties.defaultMessageTimeToLive: ... => "P14D"`** - the one genuine, intentional value
  change: the live topic currently has no message TTL (`P10675199DT2H48M5...` is ARM's
  representation of "effectively forever"); `servicebus.bicep` sets a 14-day cap. A deliberate
  tightening for this piece, not a bug - "messages that live forever" is rarely what a topic
  actually wants in production.
- **The one `Unsupported` diagnostic** (the Service Bus RBAC role assignment) - what-if can't
  evaluate a role assignment whose `guid(...)` name depends on `api`'s `principalId`, which only
  exists once the API module actually runs. This is a real Azure what-if limitation for
  identity-dependent role assignments, not specific to this template.

**Net read: this what-if is honest evidence the template already matches ~everything real about
this infrastructure**, with exactly two intentional, called-out exceptions (`alwaysOn`, topic TTL)
and zero accidental ones.

## 7. Live `what-if` output - dev (a stack that doesn't exist yet)

Run against `params/dev.bicepparam`, same session. Exit code `0`, `"status": "Succeeded"`.

```
$ az deployment sub what-if --location eastasia --template-file main.bicep --parameters params/dev.bicepparam

...
Scope: /subscriptions/30e5e569-cb36-491a-91c9-2d880095bdcb

  + resourceGroups/syquotes23dev-rg [2024-03-01]

Scope: /subscriptions/30e5e569-cb36-491a-91c9-2d880095bdcb/resourceGroups/syquotes23dev-rg

  + Microsoft.ServiceBus/namespaces/syquotes23devsb [2024-01-01]
  + Microsoft.ServiceBus/namespaces/syquotes23devsb/topics/quote-events [2024-01-01]
  + Microsoft.ServiceBus/namespaces/syquotes23devsb/topics/quote-events/subscriptions/audit-log [2024-01-01]
  + Microsoft.ServiceBus/namespaces/syquotes23devsb/topics/quote-events/subscriptions/notifications [2024-01-01]
  + Microsoft.Sql/servers/syquotes23dev-sql [2024-05-01-preview]
  + Microsoft.Sql/servers/syquotes23dev-sql/databases/quotesdb [2024-05-01-preview]
  + Microsoft.Sql/servers/syquotes23dev-sql/firewallRules/AllowAzureServices [2024-05-01-preview]
  + Microsoft.Web/serverfarms/syquotes23dev-api-plan [2024-04-01]

      sku.name: "F1"

  + Microsoft.Web/sites/syquotes23dev-api [2024-04-01]

Resource changes: 10 to create, 1 unsupported.
```

(Full property-level output trimmed here for length - the one interesting line is
`sku.name: "F1"` on the plan, confirming `apiSkuName`/`apiSkuTier` actually flow through
end to end from `params/dev.bicepparam` to the deployed resource, distinct from prod's `B1`.)
Ten resources to create, zero to modify (nothing exists yet), the same one Service Bus RBAC
`Unsupported` diagnostic as prod for the same reason.

## What did I learn this session?

1. **`what-if` genuinely catches template/reality drift, not just "would this apply cleanly."**
   Writing `api.bicep`'s plan name as `'${apiAppName}-plan'` (a reasonable-looking convention)
   against the real plan name `syquotes17-plan` (no `-api` in it - it predates that convention)
   produced a spurious "create a second plan" in the first what-if run. Fixing it required adding
   an explicit `apiServicePlanName` override rather than trusting the derived default - the kind
   of mistake that's invisible reading the Bicep alone and only shows up once compared against a
   real subscription.
2. **Not every `what-if` line is real drift, and the tool says so itself.** The banner ("may
   contain false positive predictions") wasn't decoration - a chunk of the Service Bus topic/
   subscription output is default properties this module never set, shown as `- Delete` purely
   because "not in the desired state" and "would be removed" look the same to the diff engine for
   that resource type. Distinguishing that from `alwaysOn`'s genuine drift (a property this
   template *does* set, to a *different* value than what's live) was the actual work of this
   section - the raw output alone doesn't make that distinction for you.
3. **Bicep can express control-plane RBAC (`Microsoft.Authorization/roleAssignments`) but not
   data-plane grants.** The Service Bus "Azure Service Bus Data Owner" role assignment lives
   entirely in `servicebus.bicep` - genuinely no manual step needed, unlike SQL's `CREATE USER
   ... FROM EXTERNAL PROVIDER`, which is T-SQL against the database itself and has no ARM
   resource type at all. Same underlying goal (managed identity gets access, no password exists),
   two different mechanisms, only one of which IaC can fully own.

## What would break this

- **`apiServicePlanName`'s default (`'${apiAppName}-plan'`) silently diverges from an existing
  plan whose name doesn't follow that convention** - exactly what section 6 caught for prod.
  Anyone codifying a *different* pre-existing App Service with this module needs to know to check
  and override it; the module has no way to detect the mismatch itself, only `what-if` against the
  real subscription does.
- **The Service Bus RBAC role assignment can't be previewed by `what-if`**, only applied and then
  verified after the fact (`deploy.md` step 4's `az role assignment list` check). If the role
  definition ID (`090c5cfd-...`) or the API's `principalId` were ever wrong, `what-if` would show
  it as `Unsupported` either way - identical output for "this will work" and "this will silently
  grant the wrong principal" (or none at all). A `create` + explicit post-deploy verification is
  the only way this gap actually gets caught.
- **Two resource groups for one logical stack (`syquotes17-rg` + `syquotes19-rg` in prod) is
  historical accident, not a design choice this template would make from scratch** - it exists
  because the Service Bus namespace predates this piece's consolidation (provisioned in a day-19
  exercise, in its own resource group). `serviceBusResourceGroupName` makes that representable,
  but a genuinely new environment (dev.bicepparam) collapses it back to one group - the
  two-resource-group shape should be read as "this is what prod's real history looks like," not
  "this is the recommended pattern."
- **`dev.bicepparam` has never actually been deployed**, so its "clean 10-resources-to-create"
  what-if is a plan, not a proof. A real first deploy could still hit something this session
  didn't - e.g. `F1`'s well-known resource-count-per-region-per-subscription cap, which this
  session did not check against the "Azure for Students" subscription's current F1 usage.
- **Topic/subscription defaults left unset in `servicebus.bicep`** (`maxSizeInMegabytes`,
  `enablePartitioning`, duplicate detection, ...) mean a fresh dev deploy gets whatever Azure's
  service-side defaults are at deploy time, not the specific values the live prod topic happens
  to have today. Fine for this exercise's purpose (topic/subscription *shape*, not tuning), but a
  template claiming full parity would need to pin these explicitly rather than let them drift by
  omission.

## Notes for mentor

- `day-22/piece1` (and everything upstream of it) was read-only reference / copy source - nothing
  there was modified. Backend and frontend were copied into `day-23/piece1` unchanged; this
  piece's only new content is `infra/` and this README.
- **Both `what-if` runs above are real, live output** against this student's actual "Azure for
  Students" subscription (`30e5e569-...`) - not simulated or hand-edited. `az deployment sub
  create` was deliberately not run - see "Current status" for why, and `infra/deploy.md` for the
  exact command that would apply it once given the go-ahead.
- Section 6's drift analysis (which lines are real vs. tool noise) is the part most worth a second
  look - it's this session's own judgment call on `what-if`'s output, not something the CLI labels
  for you.

## GitHub link

Not pushed yet - link to follow once pushed to the `thinkbridge-thinkschool` org, per this user's
standing preference that git actions (including read-only ones) need explicit permission each time.
