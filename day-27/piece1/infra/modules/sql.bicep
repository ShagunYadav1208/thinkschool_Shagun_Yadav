// Day 27 hardening of the target data tier: one logical SQL server hosting each module's own
// database (still five separate databases/schemas - see ../../ParkFlow/README.md's "Bounded
// Contexts" table; co-locating them on one logical server is a deployment-cost choice, not a
// module-boundary violation), with `publicNetworkAccess: 'Disabled'` and reachability only
// through a private endpoint in the VNet from network.bicep. AAD-only auth, same pattern as
// day-23/24's sql.bicep - no SQL-auth admin login exists at all.

@description('Azure region for the server and its databases.')
param location string

@description('Logical SQL server name (globally unique).')
param sqlServerName string

@description('One database per ParkFlow module - see ../../ParkFlow/README.md Bounded Contexts.')
param databaseNames array = [
  'parkflow-parking'
  'parkflow-reservation'
  'parkflow-vehicle'
  'parkflow-payment'
  'parkflow-notification'
]

@description('Object ID of the AAD principal (user or group) that becomes the SQL AAD admin.')
param aadAdminObjectId string

@description('Display name of the AAD principal set as the SQL AAD admin.')
param aadAdminName string

@description('Database SKU name, e.g. GP_S_Gen5 (General Purpose Serverless).')
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

@description('Subnet id (from network.bicep) the SQL private endpoint\'s NIC is created in.')
param privateEndpointSubnetId string

@description('Private DNS zone id (from network.bicep) for privatelink.database.windows.net.')
param privateDnsZoneId string

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
    // The one-line change that actually removes the data tier from the public internet - see
    // THREAT-MODEL.md section 3.2. Everything below this is what makes that not also break
    // connectivity for the one client (the API) that legitimately needs it.
    publicNetworkAccess: 'Disabled'
  }
}

resource sqlDatabases 'Microsoft.Sql/servers/databases@2024-05-01-preview' = [
  for name in databaseNames: {
    parent: sqlServer
    name: name
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
]

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: '${sqlServerName}-pe'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${sqlServerName}-plsc'
        properties: {
          privateLinkServiceId: sqlServer.id
          groupIds: ['sqlServer']
        }
      }
    ]
  }
}

// Registers the private IP under <sqlServerName>.database.windows.net in the linked private DNS
// zone automatically - without this, the API would resolve the server's (now-unreachable) public
// IP and every connection would time out instead of reaching the private endpoint.
resource sqlPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-database-windows-net'
        properties: {
          privateDnsZoneId: privateDnsZoneId
        }
      }
    ]
  }
}

output fullyQualifiedDomainName string = sqlServer.properties.fullyQualifiedDomainName
output serverName string = sqlServer.name
output databaseNames array = [for name in databaseNames: name]
