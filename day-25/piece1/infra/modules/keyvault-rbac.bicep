// Split out from keyvault.bicep on purpose - not a stylistic choice.
// main.bicep's `api` module needs keyvault.bicep's `redisSecretUri` OUTPUT
// (to build the App Service's Key Vault-reference app setting), while the
// role assignment that lets the API's managed identity actually READ that
// secret needs `api`'s `principalId` OUTPUT. Putting the role assignment
// inside keyvault.bicep itself would make `api` depend on `keyvault` and
// `keyvault` depend on `api` at the same time - a circular module
// dependency Bicep rejects at compile time. This module depends on both
// without either of THEM depending on each other: it takes the vault name
// and the principal ID as plain input parameters and resolves the vault via
// an `existing` reference instead of creating it.

@description('Name of the already-existing Key Vault (created by modules/keyvault.bicep) to grant access to.')
param vaultName string

@description('principalId of the managed identity to grant Key Vault Secrets User access to.')
param readerPrincipalId string

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: vaultName
}

resource secretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, readerPrincipalId, keyVaultSecretsUserRoleId)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: readerPrincipalId
    principalType: 'ServicePrincipal'
  }
}
