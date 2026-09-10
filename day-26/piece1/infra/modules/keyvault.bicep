// Key Vault for the one genuinely secret-shaped value in this app's config
// surface: Redis:ConnectionString. Every prior day left this as an empty
// value in appsettings.Production.json with a comment saying "filled in via
// the App Service's own Application Setting, never committed" - which is
// still a real secret sitting in App Service configuration in plaintext the
// moment someone actually fills it in by hand. This module gives it a real
// home: the value lives ONLY in Key Vault; the App Service's own
// Redis__ConnectionString setting (see main.bicep) becomes a
// `@Microsoft.KeyVault(SecretUri=...)` reference - a pointer, not the
// secret - resolved by the platform before the app ever starts.
//
// RBAC authorization (enableRbacAuthorization: true), not legacy access
// policies - the same "grant a role, don't maintain a policy list" pattern
// modules/servicebus.bicep already uses for Service Bus. The role
// assignment itself lives in modules/keyvault-rbac.bicep, not here - see
// that file's header comment for why it had to be split out.

@description('Azure region for the vault.')
param location string

@description('Key Vault name (globally unique, 3-24 chars).')
param vaultName string

@description('Redis connection string to store as a secret. A synthetic placeholder value is fine here - this exercise is about the wiring (Key Vault reference in, real config value out), not a live Redis instance. Never has a default: this parameter exists specifically so no value can accidentally end up committed anywhere.')
@secure()
param redisConnectionStringSecretValue string

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: vaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource redisSecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  parent: vault
  name: 'redis-connection-string'
  properties: {
    value: redisConnectionStringSecretValue
  }
}

output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
// Unversioned URI - App Service resolves it to whatever the CURRENT version
// is at read time, so rotating the secret's value later doesn't need a
// redeploy of the app setting itself.
output redisSecretUri string = redisSecret.properties.secretUri
