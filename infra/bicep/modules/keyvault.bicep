// Key Vault with RBAC authorisation and purge protection.
//
// The Key Vault is only for exceptions that require secrets. The design prefers identities without
// secrets. This module puts no secret in the vault and assigns no role.
//
// Verified in Microsoft Learn on 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.keyvault/2026-05-15/vaults
//
// WARNING, IRREVERSIBLE: `enablePurgeProtection` does not accept `false` once enabled, and
// `enableSoftDelete` does not go back to `false`. A deleted vault keeps its name reserved during
// the retention period.

@description('Azure region.')
param location string

@description('Name prefix, in lowercase. The final name has a suffix derived from the resource group.')
param namePrefix string

@description('Entra tenant identifier.')
param tenantId string

@allowed([
  'standard'
  'premium'
])
@description('Vault tier.')
param skuName string = 'standard'

@minValue(7)
@maxValue(90)
@description('Retention days after deletion (the page states 7 to 90).')
param softDeleteRetentionInDays int = 90

@description('Common tags.')
param tags object = {}

resource vault 'Microsoft.KeyVault/vaults@2026-05-15' = {
  name: '${namePrefix}-kv-${take(uniqueString(resourceGroup().id), 6)}'
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: skuName
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: softDeleteRetentionInDays
    enablePurgeProtection: true
    // TO VALIDATE: the page says `accessPolicies` is required except in createMode recover, but
    // it does not say how that combines with `enableRbacAuthorization: true`. The empty list is the
    // intention (access policies are ignored with RBAC). Confirm that the deployment accepts it.
    accessPolicies: []
    // TO VALIDATE: `publicNetworkAccess` is not set. The accepted values were not enumerated in
    // the research. Decide on a private network in Phase A.
  }
}

output vaultName string = vault.name
output vaultId string = vault.id
