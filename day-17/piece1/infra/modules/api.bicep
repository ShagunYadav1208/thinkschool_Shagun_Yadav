// App Service (Linux, .NET 10) hosting QuotesApi, with a system-assigned
// managed identity - that identity, not a connection-string password, is what
// authenticates to Azure SQL (see appsettings.Production.json's
// "Authentication=Active Directory Managed Identity" and infra/deploy.md step 3
// for granting it DB access).

param location string
param appServicePlanName string
param apiAppName string

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'B1'
    tier: 'Basic'
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
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
      ]
    }
  }
}

output defaultHostname string = api.properties.defaultHostName
output principalId string = api.identity.principalId
