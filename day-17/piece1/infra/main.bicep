// Day 17 / Piece 1 infrastructure: Static Web App (frontend) + App Service
// (QuotesApi backend, Linux, system-assigned managed identity) + Azure SQL
// (Azure AD-only auth - no SQL admin password exists anywhere, by design).
//
// Deliberately NOT wired to a custom domain (piece1's brief answer: use the
// default *.azurestaticapps.net hostname - see README "Custom domain").
//
// Deploy at subscription scope so this template can also create the
// resource group - see infra/deploy.md for the exact `az deployment sub
// create` invocation. Nothing in this file is executed yet; provisioning
// is on hold pending review (see README "Current status").

targetScope = 'subscription'

@description('Short, globally-unique-ish name segment, e.g. "syquotes17". Used to derive resource names.')
param namePrefix string

@description('Azure region for all resources.')
param location string = 'centralindia'

@description('Object ID of the AAD principal (your own user, or a group) that becomes the Azure SQL AAD admin. Required because the server has no SQL-auth admin at all - only AAD principals can manage it.')
param sqlAadAdminObjectId string

@description('Display name of the AAD principal set as the SQL AAD admin (shown in the Azure portal).')
param sqlAadAdminName string

var resourceGroupName = '${namePrefix}-rg'
var sqlServerName = '${namePrefix}-sql'
var sqlDatabaseName = 'quotesdb'
var appServicePlanName = '${namePrefix}-plan'
var apiAppName = '${namePrefix}-api'
var staticWebAppName = '${namePrefix}-swa'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
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
  }
}

module api 'modules/api.bicep' = {
  name: 'api'
  scope: rg
  params: {
    location: location
    appServicePlanName: appServicePlanName
    apiAppName: apiAppName
  }
}

module swa 'modules/swa.bicep' = {
  name: 'swa'
  scope: rg
  params: {
    location: location
    staticWebAppName: staticWebAppName
  }
}

output apiAppName string = apiAppName
output apiHostname string = api.outputs.defaultHostname
output apiPrincipalId string = api.outputs.principalId
output sqlServerFqdn string = sql.outputs.fullyQualifiedDomainName
output staticWebAppHostname string = swa.outputs.defaultHostname
output staticWebAppDeploymentTokenHint string = 'Retrieve with: az staticwebapp secrets list --name ${staticWebAppName} --query properties.apiKey -o tsv'
