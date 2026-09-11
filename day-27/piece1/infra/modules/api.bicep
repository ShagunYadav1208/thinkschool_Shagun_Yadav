// The API side of the private-endpoint story: regional VNet Integration so the App Service's
// *outbound* traffic to SQL routes through network.bicep's snet-app-integration subnet and
// resolves the private DNS zone, instead of going out over the public internet. VNet
// Integration requires at least an S1 (Standard) plan - Basic/Free/Shared don't support it.

@description('Azure region for the plan and site.')
param location string

@description('App Service Plan name.')
param appServicePlanName string

@description('Web App name (must be globally unique).')
param apiAppName string

@description('Plan SKU name - S1 or higher; VNet Integration is not available below Standard.')
param skuName string = 'S1'

@description('Plan SKU tier matching skuName.')
param skuTier string = 'Standard'

@description('ASPNETCORE_ENVIRONMENT value.')
param aspNetCoreEnvironment string = 'Production'

@description('Subnet id (from network.bicep) this Web App integrates with for outbound traffic.')
param appIntegrationSubnetId string

@description('Additional app settings, e.g. the Security__ApiKey Key Vault reference - see README "What is not built here".')
param extraAppSettings array = []

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: skuName
    tier: skuTier
  }
}

resource api 'Microsoft.Web/sites@2024-04-01' = {
  name: apiAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    virtualNetworkSubnetId: appIntegrationSubnetId
    siteConfig: {
      netFrameworkVersion: 'v10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      vnetRouteAllEnabled: true
      appSettings: concat(
        [
          {
            name: 'ASPNETCORE_ENVIRONMENT'
            value: aspNetCoreEnvironment
          }
        ],
        extraAppSettings
      )
    }
  }
}

output principalId string = api.identity.principalId
output defaultHostname string = api.properties.defaultHostName
