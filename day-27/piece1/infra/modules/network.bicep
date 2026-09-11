// Day 27: the VNet that makes "private endpoints for the data tier" an actual network boundary,
// not just a checkbox. Two subnets:
//   - snet-app-integration: delegated to Microsoft.Web/serverFarms so the App Service Plan can use
//     regional VNet Integration (outbound only - the API's *outbound* calls to SQL leave through
//     this subnet instead of the public internet).
//   - snet-private-endpoints: where the SQL private endpoint's NIC actually lives. Private
//     endpoint policies must be disabled on this subnet - Azure blocks NSG/route-table
//     enforcement on a private-endpoint subnet by default otherwise.
// A private DNS zone for privatelink.database.windows.net is linked to the VNet so the API
// resolves the SQL server's private IP without any client-side configuration - this is what
// makes swapping public->private endpoint transparent to Program.cs's connection string.

@description('Azure region for the VNet and its subnets.')
param location string

@description('VNet name.')
param vnetName string

@description('VNet address space.')
param vnetAddressPrefix string = '10.20.0.0/16'

@description('App Service VNet Integration subnet address prefix.')
param appIntegrationSubnetPrefix string = '10.20.1.0/24'

@description('Private endpoint subnet address prefix.')
param privateEndpointSubnetPrefix string = '10.20.2.0/24'

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [vnetAddressPrefix]
    }
    subnets: [
      {
        name: 'snet-app-integration'
        properties: {
          addressPrefix: appIntegrationSubnetPrefix
          delegations: [
            {
              name: 'appServicePlanDelegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'snet-private-endpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

// environment().suffixes.sqlServerHostname (not a hardcoded 'database.windows.net') so this
// still resolves correctly if this template is ever deployed against a sovereign cloud
// (Azure Government, Azure China) instead of public Azure.
resource sqlPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  // suffixes.sqlServerHostname already includes its own leading dot (e.g. ".database.windows.net").
  name: 'privatelink${environment().suffixes.sqlServerHostname}'
  location: 'global'
}

resource sqlPrivateDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: sqlPrivateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

output vnetId string = vnet.id
output appIntegrationSubnetId string = vnet.properties.subnets[0].id
output privateEndpointSubnetId string = vnet.properties.subnets[1].id
output sqlPrivateDnsZoneId string = sqlPrivateDnsZone.id
