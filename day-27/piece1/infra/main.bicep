// Day 27: the target Azure architecture for ParkFlow (day-22/piece2's capstone), hardened per
// THREAT-MODEL.md section 3.2 - the data tier (five SQL databases, one per module) sits behind a
// private endpoint, reachable only through the VNet the API is integrated into. Written and
// `az bicep build`-clean, never deployed - see README.md, "Why undeployed" (the same disabled
// Azure for Students subscription blocking day-25/26's Key Vault and App Insights work).
//
// Deploy at subscription scope so this template can create its own resource group, matching the
// day-23/24 convention for this student's other exercises.

targetScope = 'subscription'

@description('Environment name - "dev" or "prod". Used for tagging only; every resource name comes from its own parameter.')
@allowed([
  'dev'
  'prod'
])
param environmentName string = 'dev'

@description('Resource group for every resource in this deployment.')
param resourceGroupName string

@description('Azure region for every resource.')
param location string = 'eastus'

@description('Object ID of the AAD principal that becomes the Azure SQL AAD admin.')
param sqlAadAdminObjectId string

@description('Display name of the AAD principal set as the SQL AAD admin.')
param sqlAadAdminName string

@description('VNet name.')
param vnetName string = 'vnet-parkflow-${environmentName}'

@description('SQL logical server name (globally unique).')
param sqlServerName string

@description('SQL database SKU name.')
param sqlSkuName string = 'GP_S_Gen5'

@description('SQL database SKU tier matching sqlSkuName.')
param sqlSkuTier string = 'GeneralPurpose'

@description('SQL database SKU capacity (vCores for GeneralPurpose, DTUs for Basic).')
param sqlSkuCapacity int = 1

@description('App Service Plan name. Defaults to "<apiAppName>-plan".')
param apiServicePlanName string = '${apiAppName}-plan'

@description('Web App name (globally unique).')
param apiAppName string

@description('App Service Plan SKU name - must be Standard (S1) or higher for VNet Integration.')
param apiSkuName string = 'S1'

@description('App Service Plan SKU tier matching apiSkuName.')
param apiSkuTier string = 'Standard'

var tags = {
  environment: environmentName
  project: 'thinkschool-parkflow'
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module network 'modules/network.bicep' = {
  name: 'network'
  scope: rg
  params: {
    location: location
    vnetName: vnetName
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    location: location
    sqlServerName: sqlServerName
    aadAdminObjectId: sqlAadAdminObjectId
    aadAdminName: sqlAadAdminName
    skuName: sqlSkuName
    skuTier: sqlSkuTier
    skuCapacity: sqlSkuCapacity
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneId: network.outputs.sqlPrivateDnsZoneId
  }
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
    appIntegrationSubnetId: network.outputs.appIntegrationSubnetId
    // Security__ApiKey deliberately not set here: this piece's scope is the data-tier private
    // endpoint, not standing up a Key Vault for ParkFlow. day-25/piece1's keyvault.bicep is the
    // already-established pattern for wiring a real secret in as a
    // `@Microsoft.KeyVault(SecretUri=...)` reference app setting - see README.md, "What is not
    // built here".
    extraAppSettings: []
  }
}

output AZURE_RESOURCE_GROUP string = rg.name
output apiAppName string = apiAppName
output apiHostname string = api.outputs.defaultHostname
output apiPrincipalId string = api.outputs.principalId
output sqlServerFqdn string = sql.outputs.fullyQualifiedDomainName
output sqlDatabaseNames array = sql.outputs.databaseNames
