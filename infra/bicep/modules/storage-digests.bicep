// Storage account and container for the Azure SQL ledger digests.
//
// Base destination of the digests: Blob with an immutability policy (WORM) at container level.
// Azure Confidential Ledger remains an option to be assessed in Phase A and is not created here
// (its Bicep type and apiVersion were not verified).
//
// Verified in Microsoft Learn on 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.storage/2026-09-01/storageaccounts
// https://learn.microsoft.com/en-us/azure/templates/microsoft.storage/2026-09-01/storageaccounts/blobservices/containers
// https://learn.microsoft.com/en-us/azure/templates/microsoft.storage/2026-09-01/storageaccounts/blobservices/containers/immutabilitypolicies
// https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management
// https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-container-level-worm-policies
//
// IMMUTABILITY POLICY: NOT LOCKED, ON PURPOSE
// -------------------------------------------------------
// The policy created here stays in the unlocked state. The documentation says that an unlocked
// policy protects against deletion and overwriting, but can be shortened or deleted, and
// recommends it only for short-lived tests. Locking is a manual step, done after the retention has
// been approved, because:
// - once locked, the policy cannot be deleted and the period can only increase (a maximum of five
//   increases on a container-level policy);
// - once locked, `allowProtectedAppendWrites` can no longer be changed;
// - the storage account cannot be deleted while there is a container with a locked policy and
//   blobs present, nor before the retention has expired;
// - the retention has to be decided (GDPR, cost, residency) before it becomes irreversible.
// It is not confirmed that the lock can be declared in Bicep (NOT VERIFIED in the research). The
// documented command is `az storage container immutability-policy lock ... --if-match $etag`. See
// README.

@description('Azure region.')
param location string

@description('Name prefix, in lower case and without hyphens, with at most 9 characters (24-character limit on the account name).')
param namePrefix string

// The ledger documentation states that automatic digest upload does not support LRS accounts.
// There is no default value: redundancy is a Phase A decision.
@allowed([
  'Standard_GRS'
  'Standard_GZRS'
  'Standard_RAGRS'
  'Standard_RAGZRS'
])
@description('Account redundancy. LRS is not supported for automatic digests.')
param storageSkuName string

// Documented range: 1 to 146000 days. No default value: the documentation warns that the 1-day
// value in the quickstart is not suitable for production, and the decision belongs to Phase A.
@minValue(1)
@maxValue(146000)
@description('Retention, in days, of the container immutability policy (before the lock).')
param immutabilityPeriodInDays int

@description('Common tags.')
param tags object = {}

// Documented fixed name: the Azure SQL logical server uses this container.
var containerName = 'sqldbledgerdigests'
var accountName = toLower('${namePrefix}dg${uniqueString(resourceGroup().id)}')

resource account 'Microsoft.Storage/storageAccounts@2026-09-01' = {
  name: accountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: storageSkuName
  }
  tags: tags
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    defaultToOAuthAuthentication: true
    // TO VALIDATE: the documentation says that the SQL server writes the digests with RBAC (Storage
    // Blob Data Contributor role). It was not verified that the upload works with shared keys
    // switched off. If it fails in Phase C, this line is the first to review.
    allowSharedKeyAccess: false
    // TO VALIDATE: `publicNetworkAccess` and the account firewall are not set. The behaviour of
    // digest upload with a restricted network was not verified.
  }
}

// TO VALIDATE: the documentation says that the server "creates a new container" and that the policy
// is configured "after provisioning". Whether the container can be created here, with a policy,
// before the first upload is not confirmed. If the upload fails because the container already
// exists, remove these two resources and create the policy after the first upload, as in the
// quickstart.
//
// The `blobServices` type is not in the verified type table. The full names below avoid declaring
// it and use only the verified types.
resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2026-09-01' = {
  name: '${account.name}/default/${containerName}'
  properties: {
    publicAccess: 'None'
  }
}

resource immutability 'Microsoft.Storage/storageAccounts/blobServices/containers/immutabilityPolicies@2026-09-01' = {
  name: '${container.name}/default'
  properties: {
    immutabilityPeriodSinceCreationInDays: immutabilityPeriodInDays
    // Required for the digests: they are append blobs.
    allowProtectedAppendWrites: true
    // No `state` and no lock: see the header of this file.
  }
}

output accountName string = account.name
output accountId string = account.id
output containerName string = containerName
// Blob endpoint built from the environment suffix. It is the value that `ledgerDigestUploads`
// expects in `digestStorageEndpoint`.
output blobEndpoint string = 'https://${account.name}.blob.${environment().suffixes.storage}'
