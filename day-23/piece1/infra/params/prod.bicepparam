using '../main.bicep'

// Points at this repo's real, already-running production infrastructure
// (day-17/19's syquotes17-rg + syquotes19-rg) - see README "Current status"
// for the what-if run this produces and what it does/doesn't show as drift.
//
// sqlAadAdminObjectId/sqlAadAdminName are read from environment variables,
// never hardcoded here, so this file carries no personal identifier. Set
// them before deploying:
//   export SQL_AAD_ADMIN_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
//   export SQL_AAD_ADMIN_NAME=$(az ad signed-in-user show --query userPrincipalName -o tsv)

param environmentName = 'prod'

param resourceGroupName = 'syquotes17-rg'
param serviceBusResourceGroupName = 'syquotes19-rg'
param location = 'eastasia'

param sqlAadAdminObjectId = readEnvironmentVariable('SQL_AAD_ADMIN_OBJECT_ID')
param sqlAadAdminName = readEnvironmentVariable('SQL_AAD_ADMIN_NAME')

param apiAppName = 'syquotes17-api'
param apiServicePlanName = 'syquotes17-plan'
param apiSkuName = 'B1'
param apiSkuTier = 'Basic'
param corsAllowedOrigin = 'https://black-desert-0fde3f100.7.azurestaticapps.net'

param sqlServerName = 'syquotes17-sql'
param sqlDatabaseName = 'quotesdb'
param sqlSkuName = 'GP_S_Gen5'
param sqlSkuTier = 'GeneralPurpose'
param sqlSkuCapacity = 1

param serviceBusNamespaceName = 'syquotes19sb'
param serviceBusSkuName = 'Standard'
param serviceBusTopicName = 'quote-events'
