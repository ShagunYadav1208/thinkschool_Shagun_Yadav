// Azure SQL, Azure-AD-only authentication. `administrators.azureADOnlyAuthentication:
// true` means there is no SQL-auth admin login at all for this server - not "a
// password stored somewhere else," an actual absence of that auth mode. The App
// Service's managed identity is granted access as a database user AFTER this
// deploys (see infra/deploy.md step 3 - that grant is a T-SQL statement run as
// the AAD admin, which Bicep/ARM can't express directly).

@description('Azure region for the server and database.')
param location string

@description('Logical SQL server name (globally unique).')
param sqlServerName string

@description('Database name.')
param sqlDatabaseName string

@description('Object ID of the AAD principal (user or group) that becomes the SQL AAD admin.')
param aadAdminObjectId string

@description('Display name of the AAD principal set as the SQL AAD admin.')
param aadAdminName string

@description('Database SKU name, e.g. GP_S_Gen5 (General Purpose Serverless) for dev/prod, or Basic for a cheaper throwaway dev stack.')
param skuName string = 'GP_S_Gen5'

@description('Database SKU tier matching skuName.')
param skuTier string = 'GeneralPurpose'

@description('Database SKU family - only meaningful for vCore SKUs.')
param skuFamily string = 'Gen5'

@description('Database SKU capacity (vCores for GeneralPurpose, DTUs for Basic/Standard).')
param skuCapacity int = 1

@description('Minutes of inactivity before a serverless database auto-pauses. -1 disables auto-pause; ignored for non-serverless SKUs.')
param autoPauseDelay int = 60

@description('Minimum vCores while running (serverless only, ignored for non-serverless SKUs).')
param minCapacity string = '0.5'

var isServerless = contains(skuName, '_S_')

resource sqlServer 'Microsoft.Sql/servers@2024-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: aadAdminName
      sid: aadAdminObjectId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
  }
}

// Lets Azure resources (the App Service's outbound IPs, which change) reach
// the server without a per-IP firewall rule - standard for App Service + Azure
// SQL, since the actual authorization boundary here is the AAD token check on
// login, not the network layer.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2024-05-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: skuName
    tier: skuTier
    family: skuFamily
    capacity: skuCapacity
  }
  properties: isServerless
    ? {
        autoPauseDelay: autoPauseDelay
        minCapacity: json(minCapacity)
      }
    : {}
}

output fullyQualifiedDomainName string = sqlServer.properties.fullyQualifiedDomainName
output serverName string = sqlServer.name
output databaseName string = sqlDatabase.name
