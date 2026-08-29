// Azure SQL, Azure-AD-only authentication. `administrators.azureADOnlyAuthentication:
// true` means there is no SQL-auth admin login at all for this server - not "a
// password stored somewhere else," an actual absence of that auth mode. The App
// Service's managed identity is granted access as a database user AFTER this
// deploys (see infra/deploy.md step 3 - that grant is a T-SQL statement run as
// the AAD admin, which Bicep/ARM can't express directly).

param location string
param sqlServerName string
param sqlDatabaseName string
param aadAdminObjectId string
param aadAdminName string

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
    // General Purpose Serverless, smallest vCore tier - auto-pauses when idle,
    // which matters on a student subscription's limited credit.
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
  }
}

output fullyQualifiedDomainName string = sqlServer.properties.fullyQualifiedDomainName
