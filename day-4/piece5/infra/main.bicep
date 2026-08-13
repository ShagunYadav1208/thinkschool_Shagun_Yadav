// Infrastructure for Day 4 Piece 5: an Application Insights resource (workspace-based, the only
// kind Azure creates today), an email action group, and a log alert on POST /api/quotes latency.
//
// Not applied against a live subscription from this exercise — provisioning real Azure resources
// needs a real subscription and `az login`, which this environment doesn't have. Deploy it with:
//
//   az deployment group create \
//     --resource-group <your-rg> \
//     --template-file infra/main.bicep \
//     --parameters alertEmail=you@example.com

@description('Base name used to derive resource names (App Insights, Log Analytics, action group).')
param appName string = 'quotes-integration-api'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Email address that receives the response-time alert.')
param alertEmail string

@description('Response-time threshold in milliseconds that triggers the alert.')
param responseTimeThresholdMs int = 500

@description('Evaluation window for the average, in minutes.')
param evaluationWindowMinutes int = 5

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${appName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

// The connection string above is the only thing the app needs — and it's read out of Key Vault at
// runtime (see Program.cs), never written into a config file. This resource block is where it
// would be provisioned into Key Vault as part of the same deployment in a real setup:
//
// resource kvSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
//   name: '<key-vault-name>/ApplicationInsights--ConnectionString'
//   properties: {
//     value: appInsights.properties.ConnectionString
//   }
// }

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${appName}-alerts'
  location: 'global'
  properties: {
    groupShortName: 'quotesAlert'
    enabled: true
    emailReceivers: [
      {
        name: 'OnCallEmail'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// "Average response time of POST /api/quotes exceeds 500ms over 5 minutes" is a per-endpoint
// average, not a resource-level metric Azure Monitor exposes by name — so this is a log alert
// (scheduled KQL query) against the requests table, not a metric alert.
resource responseTimeAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${appName}-post-quotes-latency'
  location: location
  properties: {
    displayName: 'POST /api/quotes average response time > ${responseTimeThresholdMs}ms'
    description: 'Pages when POST /api/quotes averages slower than ${responseTimeThresholdMs}ms over ${evaluationWindowMinutes} minutes. Everything else about this endpoint is dashboard-only.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT${evaluationWindowMinutes}M'
    windowSize: 'PT${evaluationWindowMinutes}M'
    scopes: [
      appInsights.id
    ]
    criteria: {
      allOf: [
        {
          query: '''
requests
| where name == "POST /api/quotes"
| summarize AvgDurationMs = avg(duration) by bin(timestamp, 5m)
'''
          timeAggregation: 'Average'
          metricMeasureColumn: 'AvgDurationMs'
          operator: 'GreaterThan'
          threshold: responseTimeThresholdMs
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
    autoMitigate: true
  }
}

output appInsightsConnectionString string = appInsights.properties.ConnectionString
