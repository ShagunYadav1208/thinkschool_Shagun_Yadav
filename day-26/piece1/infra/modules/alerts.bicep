// Day 26: the error-rate alert as a real, deployable resource - not just a KQL string sitting in
// a README. Uses the same query pasted in the README's "KQL - alert on error-rate" section, so
// the two can never quietly drift apart; if you change one, change both.

@description('Azure region for the alert rule.')
param location string

@description('Resource ID of the Application Insights component to scope this alert to.')
param appInsightsId string

@description('Error-rate percentage threshold that triggers the alert.')
param errorRateThreshold int = 5

@description('Resource IDs of Action Groups to notify - empty by default (no one configured yet to receive it). A real deployment would set this to a real Action Group (email/SMS/webhook).')
param actionGroupIds array = []

resource errorRateAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'quotesapi-error-rate'
  location: location
  properties: {
    displayName: 'QuotesApi - error rate above threshold'
    description: 'Fires when the request error rate over the last 5 minutes exceeds ${errorRateThreshold}%.'
    severity: 2
    enabled: true
    scopes: [
      appInsightsId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      allOf: [
        {
          query: '''
requests
| where timestamp > ago(5m)
| summarize total = count(), failed = countif(success == false)
| extend errorRatePercent = round(100.0 * failed / total, 2)
| where errorRatePercent > ${errorRateThreshold}
'''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: actionGroupIds
    }
  }
}

output alertId string = errorRateAlert.id
