// Day 26: Application Insights (workspace-based, the only kind Azure still lets you create -
// "classic" non-workspace App Insights has been retired) backed by its own Log Analytics
// workspace. QuotesApi's OpenTelemetry SDK (see Observability/TelemetryExtensions.cs) sends
// traces/metrics/logs here via the Azure Monitor exporter whenever `AppInsights:ConnectionString`
// (this module's own `connectionString` output) is non-empty - empty by default, same
// feature-flag-by-absence pattern day-21's Redis connection string and day-25's Key Vault secret
// both used.

@description('Azure region for the workspace and App Insights resource.')
param location string

@description('Log Analytics workspace name.')
param workspaceName string

@description('Application Insights resource name.')
param appInsightsName string

@description('Days to retain logs/traces - kept short deliberately, this is a demo workload, not a compliance-driven retention requirement.')
param retentionInDays int = 30

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: workspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

output connectionString string = appInsights.properties.ConnectionString
output appInsightsName string = appInsights.name
output appInsightsId string = appInsights.id
output workspaceId string = workspace.id
