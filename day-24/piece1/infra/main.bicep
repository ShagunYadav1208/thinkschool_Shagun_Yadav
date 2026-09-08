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
