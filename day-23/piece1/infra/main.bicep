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
