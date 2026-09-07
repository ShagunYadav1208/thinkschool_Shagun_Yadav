using '../main.bicep'

// A separate, cheap, throwaway stack - its own resource group, Free-tier App
// Service plan (no alwaysOn), and a local-dev CORS origin. Never deployed as
// part of this exercise (see README "Current status") - included to satisfy
// "separate dev/prod parameter files" and to show the same template actually
// producing a materially different plan for a different environment.
//
// sqlAadAdminObjectId/sqlAadAdminName are read from environment variables,
// never hardcoded here - same convention as prod.bicepparam.

param environmentName = 'dev'

param resourceGroupName = 'syquotes23dev-rg'
param serviceBusResourceGroupName = 'syquotes23dev-rg'
param location = 'eastasia'

param sqlAadAdminObjectId = readEnvironmentVariable('SQL_AAD_ADMIN_OBJECT_ID')
param sqlAadAdminName = readEnvironmentVariable('SQL_AAD_ADMIN_NAME')

param apiAppName = 'syquotes23dev-api'
param apiSkuName = 'F1'
param apiSkuTier = 'Free'
param corsAllowedOrigin = 'http://localhost:4200'

param sqlServerName = 'syquotes23dev-sql'
param sqlDatabaseName = 'quotesdb'
param sqlSkuName = 'GP_S_Gen5'
param sqlSkuTier = 'GeneralPurpose'
param sqlSkuCapacity = 1

param serviceBusNamespaceName = 'syquotes23devsb'
param serviceBusSkuName = 'Standard'
param serviceBusTopicName = 'quote-events'
