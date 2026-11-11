// Agent Portifolio: Azure TEST environment (Phase C).
//
// THIS BICEP HAS NEVER BEEN APPLIED. There is no subscription. The syntax has not been validated by
// a compiler. Do not apply before the Phase A gates (scope, licences and permissions) are approved
// and the irreversible choices listed in the README are decided (isLedgerOn, digest retention,
// Key Vault purge protection, Entra-only authentication on SQL).
//
// Scope: resource group. The resource group is created outside this file.
//
// Everything marked `TO VALIDATE` was not verified in Microsoft Learn in the research of
// 2026-11-11 (research/03). There are no invented values: what is not known stays in a comment or
// in a parameter with no default value.
//
// There are no secrets in this file or in the modules. The parameters below have no default value
// when the choice has consequences for cost, security or irreversibility.

targetScope = 'resourceGroup'

// ---------- General ----------

@minLength(3)
@maxLength(9)
@description('Name prefix: lowercase letters and digits only, at most 9 characters (the storage account name has a limit of 24).')
param namePrefix string

@description('Azure region. By default, that of the resource group.')
param location string = resourceGroup().location

@description('Entra tenant identifier.')
param tenantId string

@minLength(1)
@description('Object IDs of the platform administrators. The first is the Entra administrator of the SQL server and should be a group. The roles of the others are assigned by hand (README).')
param adminObjectIds array

@description('Name (login) of the SQL Entra administrator, as it appears in the directory.')
param sqlAdminLogin string

@allowed([
  'Application'
  'Group'
  'User'
])
@description('Type of the SQL administrator principal.')
param sqlAdminPrincipalType string

@description('Common tags.')
param tags object = {}

// ---------- Applications ----------

@description('Container image (single, with the three services). Reference without secrets.')
param containerImage string

@description('Container registry server. Empty if no identity is needed to pull the image.')
param registryServer string = ''

@description('Port the image listens on.')
param targetPort int = 8080

@description('Audience expected by the control-api (for example the application ID URI).')
param controlApiAudience string

@description('Audience expected by the policy-executor.')
param executorAudience string

@description('Adapters mode (Adapters__Mode). The code accepts only `Simulation` and any other value prevents startup. There is no real adapter for Entra or Graph: do not use a value that suggests otherwise.')
param adaptersMode string

// ---------- Database and digests ----------

@description('Database SKU name. No default value (TO VALIDATE in the reference).')
param databaseSkuName string

@description('Ledger database (all tables are ledger tables). IRREVERSIBLE.')
param isLedgerOn bool

@allowed([
  'Geo'
  'GeoZone'
  'Local'
  'Zone'
])
@description('Backup storage redundancy.')
param backupStorageRedundancy string = 'Geo'

@allowed([
  'Standard_GRS'
  'Standard_GZRS'
  'Standard_RAGRS'
  'Standard_RAGZRS'
])
@description('Redundancy of the digests account. LRS is not supported for automatic digests.')
param storageSkuName string

@minValue(1)
@maxValue(146000)
@description('Retention, in days, of the digests immutability policy, before the lock.')
param digestImmutabilityDays int

@description('Turns on the automatic digest upload. Leave false on the first deployment: the write role of the SQL server on the account is manual (README).')
param enableLedgerDigestUpload bool = false

// ---------- Service Bus, monitoring, Key Vault ----------

@allowed([
  'Standard'
  'Premium'
])
@description('Service Bus tier. Duplicate detection requires Standard or Premium.')
param serviceBusSkuName string

@description('Log Analytics log retention, in days. No default value.')
param logRetentionInDays int

@description('Log Analytics daily ingestion limit in GB. 0 sets no limit.')
param logDailyQuotaGb int = 0

// ---------- APIM ----------

@allowed([
  'Basic'
  'BasicV2'
  'Consumption'
  'Developer'
  'Isolated'
  'Premium'
  'Standard'
  'StandardV2'
])
@description('APIM tier. No default value.')
param apimSkuName string

@description('APIM SKU units. For Consumption it must be 0.')
param apimSkuCapacity int

@description('APIM publisher email.')
param apimPublisherEmail string

@description('APIM publisher name.')
param apimPublisherName string

@minLength(1)
@description('appId of the client applications authorised at the gateway (agent identities).')
param executorClientApplicationIds array

// ---------- Variables ----------

// The code uses the issuer declared in the metadata of this authority (Auth__Authority). The version
// of the issuer of the real tokens is TO VALIDATE in the tenant. The environment `loginEndpoint`
// ends with a slash.
var authAuthority = '${environment().authentication.loginEndpoint}${tenantId}/v2.0'

// ---------- Modules, in dependency order ----------

module monitoring 'modules/monitoring.bicep' = {
  name: 'ap-monitoring'
  params: {
    location: location
    namePrefix: namePrefix
    retentionInDays: logRetentionInDays
    dailyQuotaGb: logDailyQuotaGb
    tags: tags
  }
}

module identities 'modules/identities.bicep' = {
  name: 'ap-identities'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

module keyvault 'modules/keyvault.bicep' = {
  name: 'ap-keyvault'
  params: {
    location: location
    namePrefix: namePrefix
    tenantId: tenantId
    tags: tags
  }
}

module storageDigests 'modules/storage-digests.bicep' = {
  name: 'ap-storage-digests'
  params: {
    location: location
    namePrefix: namePrefix
    storageSkuName: storageSkuName
    immutabilityPeriodInDays: digestImmutabilityDays
    tags: tags
  }
}

module sql 'modules/sql.bicep' = {
  name: 'ap-sql'
  params: {
    location: location
    namePrefix: namePrefix
    tenantId: tenantId
    entraAdminObjectId: adminObjectIds[0]
    entraAdminLogin: sqlAdminLogin
    entraAdminPrincipalType: sqlAdminPrincipalType
    databaseSkuName: databaseSkuName
    isLedgerOn: isLedgerOn
    backupStorageRedundancy: backupStorageRedundancy
    enableLedgerDigestUpload: enableLedgerDigestUpload
    digestStorageEndpoint: storageDigests.outputs.blobEndpoint
    tags: tags
  }
}

module servicebus 'modules/servicebus.bicep' = {
  name: 'ap-servicebus'
  params: {
    location: location
    namePrefix: namePrefix
    serviceBusSkuName: serviceBusSkuName
    tags: tags
  }
}

module containerapps 'modules/containerapps.bicep' = {
  name: 'ap-containerapps'
  params: {
    location: location
    namePrefix: namePrefix
    logAnalyticsWorkspaceName: monitoring.outputs.workspaceName
    containerImage: containerImage
    registryServer: registryServer
    targetPort: targetPort
    tenantId: tenantId
    authAuthority: authAuthority
    controlApiAudience: controlApiAudience
    executorAudience: executorAudience
    adaptersMode: adaptersMode
    sqlServerFqdn: sql.outputs.serverFqdn
    databaseName: sql.outputs.databaseName
    controlIdentity: {
      id: identities.outputs.control.id
      clientId: identities.outputs.control.clientId
    }
    executorIdentity: {
      id: identities.outputs.executor.id
      clientId: identities.outputs.executor.clientId
    }
    workerIdentity: {
      id: identities.outputs.worker.id
      clientId: identities.outputs.worker.clientId
    }
    tags: tags
  }
}

module apim 'modules/apim.bicep' = {
  name: 'ap-apim'
  params: {
    location: location
    namePrefix: namePrefix
    tenantId: tenantId
    publisherEmail: apimPublisherEmail
    publisherName: apimPublisherName
    skuName: apimSkuName
    skuCapacity: apimSkuCapacity
    executorBackendUrl: 'https://${containerapps.outputs.executorFqdn}'
    executorAudience: executorAudience
    executorClientApplicationIds: executorClientApplicationIds
    tags: tags
  }
}

// ---------- Outputs (no secrets) ----------
// They serve the manual steps in the README: role assignments and T-SQL users.

output sqlServerName string = sql.outputs.serverName
output sqlServerPrincipalId string = sql.outputs.serverPrincipalId
output digestStorageAccountName string = storageDigests.outputs.accountName
output serviceBusNamespaceName string = servicebus.outputs.namespaceName
output keyVaultName string = keyvault.outputs.vaultName
output controlIdentityPrincipalId string = identities.outputs.control.principalId
output executorIdentityPrincipalId string = identities.outputs.executor.principalId
output workerIdentityPrincipalId string = identities.outputs.worker.principalId
output controlIdentityName string = identities.outputs.control.name
output executorIdentityName string = identities.outputs.executor.name
output workerIdentityName string = identities.outputs.worker.name
