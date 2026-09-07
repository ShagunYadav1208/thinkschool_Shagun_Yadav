// App Service (Linux) hosting QuotesApi, with a system-assigned managed
// identity - that identity, not a connection-string password, is what
// authenticates to both Azure SQL (Active Directory Managed Identity auth)
// and Service Bus (Azure Service Bus Data Owner role, granted in
// modules/servicebus.bicep using this module's principalId output).

@description('Azure region for the plan and site.')
param location string

@description('App Service Plan name.')
param appServicePlanName string

@description('App Service (Web App) name.')
param apiAppName string

@description('Plan SKU name, e.g. B1 (dev/prod default) or F1 (free tier for a throwaway dev stack).')
param skuName string = 'B1'

@description('Plan SKU tier matching skuName.')
param skuTier string = 'Basic'

@description('.NET runtime version tag for linuxFxVersion, e.g. DOTNETCORE|10.0.')
param dotnetVersion string = 'DOTNETCORE|10.0'

@description('ASPNETCORE_ENVIRONMENT value for this environment.')
param aspNetCoreEnvironment string = 'Production'

@description('Extra app settings merged in on top of the required ones (ASPNETCORE_ENVIRONMENT). Use for environment-specific values that must not live in source, e.g. Redis__ConnectionString.')
param extraAppSettings array = []

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: skuName
    tier: skuTier
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource api 'Microsoft.Web/sites@2024-04-01' = {
  name: apiAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: dotnetVersion
      alwaysOn: skuTier != 'Free'
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

output defaultHostname string = api.properties.defaultHostName
output principalId string = api.identity.principalId
output appName string = api.name
