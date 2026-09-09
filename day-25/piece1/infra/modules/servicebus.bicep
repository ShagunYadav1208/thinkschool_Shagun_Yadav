// Service Bus namespace + one topic + its subscriptions, matching
// QuotesApi's ServiceBusMessaging module (topic "quote-events", fanned out
// to an audit-log subscription and a notifications subscription - see
// ServiceBusMessaging/ServiceBusOptions.cs). No connection-string secret
// anywhere: the API authenticates with its own managed identity
// (DefaultAzureCredential in Azure - see Extensions/InfrastructureExtensions.cs),
// granted access here via an "Azure Service Bus Data Owner" role assignment
// scoped to this namespace, the same managed-identity-over-password pattern
// modules/sql.bicep uses for Azure SQL.

@description('Azure region for the namespace.')
param location string

@description('Service Bus namespace name (globally unique).')
param namespaceName string

@description('Namespace SKU. Standard is required for topics/subscriptions - Basic only supports queues.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param skuName string = 'Standard'

@description('Topic name.')
param topicName string = 'quote-events'

@description('Subscriptions to create under the topic, each as {name, lockDuration, maxDeliveryCount}.')
param subscriptions array = [
  {
    name: 'audit-log'
    lockDuration: 'PT30S'
    maxDeliveryCount: 5
  }
  {
    name: 'notifications'
    lockDuration: 'PT15S'
    maxDeliveryCount: 3
  }
]

@description('principalId of the managed identity (e.g. the API app\'s system-assigned identity) to grant data-plane access to. Empty string skips the role assignment.')
param dataOwnerPrincipalId string = ''

var serviceBusDataOwnerRoleId = '090c5cfd-751d-490a-894a-3ce6f1109419'

resource namespaceRes 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespaceRes
  name: topicName
  properties: {
    defaultMessageTimeToLive: 'P14D'
  }
}

resource topicSubscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = [
  for sub in subscriptions: {
    parent: topic
    name: sub.name
    properties: {
      lockDuration: sub.lockDuration
      maxDeliveryCount: sub.maxDeliveryCount
      deadLetteringOnMessageExpiration: false
      deadLetteringOnFilterEvaluationExceptions: true
    }
  }
]

resource dataOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(dataOwnerPrincipalId)) {
  name: guid(namespaceRes.id, dataOwnerPrincipalId, serviceBusDataOwnerRoleId)
  scope: namespaceRes
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataOwnerRoleId)
    principalId: dataOwnerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output fullyQualifiedNamespace string = '${namespaceRes.name}.servicebus.windows.net'
output namespaceName string = namespaceRes.name
output topicName string = topic.name
