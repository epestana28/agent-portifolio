// Azure SQL server with Entra-only authentication, database and digest upload configuration.
//
// Verified in Microsoft Learn on 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers
// https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/azureadonlyauthentications
// https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/databases
// https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/databases/ledgerdigestuploads
// https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication
//
// What this module deliberately does NOT do:
// - It does not create database users. `CREATE USER ... FROM EXTERNAL PROVIDER` is T-SQL and cannot
//   be declared in Bicep. It remains a post-deploy step (README).
// - It does not create the role that gives the SQL server write access to the digest account
//   (Storage Blob Data Contributor). It remains a manual step (README). That is why digest upload
//   is off by default (`enableLedgerDigestUpload`) and is turned on in a second deploy.
// - It does not create firewall rules or a private endpoint (types not verified). Without one of
//   them, no client can connect over the public network. Network decision in Phase A.

@description('Azure region.')
param location string

@description('Name prefix, in lower case.')
param namePrefix string

@description('Entra tenant identifier.')
param tenantId string

@description('Object ID of the Entra administrator of the server. A group is recommended.')
param entraAdminObjectId string

@description('Name (login) of the Entra administrator, as it appears in the directory.')
param entraAdminLogin string

@allowed([
  'Application'
  'Group'
  'User'
])
@description('Type of the administrator principal.')
param entraAdminPrincipalType string

@description('Name of the database.')
param databaseName string = 'agentportifolio'

// TO VALIDATE: the `sku.name` values are not enumerated in the reference. The T-SQL example in the
// documentation uses the service objective 'GP_Gen5_2', but that does not prove it is the
// `sku.name` accepted by ARM. No default value.
@description('Name of the database SKU.')
param databaseSkuName string

// IRREVERSIBLE DECISION. With `true`, ALL the tables in the database become ledger tables,
// including the operational ones and the migration ones (updatable by default, with a history
// table that cannot be deleted). With `false`, tables are chosen one by one with
// WITH (LEDGER = ON (APPEND_ONLY = ON)). Neither option can be reverted. No default value: decide
// in Phase A.
@description('Ledger database (all tables are ledger tables). It cannot be changed afterwards.')
param isLedgerOn bool

@allowed([
  'Geo'
  'GeoZone'
  'Local'
  'Zone'
])
@description('Redundancy of the backup storage. Decision tied to the RPO.')
param backupStorageRedundancy string = 'Geo'

@description('Turns on automatic digest upload. Only after the write role has been assigned to the server identity.')
param enableLedgerDigestUpload bool = false

@description('Blob endpoint of the digest account (or URI of the Confidential Ledger).')
param digestStorageEndpoint string

@description('Common tags.')
param tags object = {}

var serverName = '${namePrefix}-sql-${take(uniqueString(resourceGroup().id), 6)}'

resource server 'Microsoft.Sql/servers@2025-01-01' = {
  name: serverName
  location: location
  tags: tags
  // TO VALIDATE: the `identity` block of the server is not in the extracted table. The digest
  // documentation speaks of the "logical server (System Identity)" as the identity that writes to
  // the storage account. Whether a user-assigned identity works for the upload: NOT VERIFIED.
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    administrators: {
      // TO VALIDATE: the reference may require `administratorType: 'ActiveDirectory'`. The property
      // is not in the extracted list, so it is not set here. Confirm before applying.
      principalType: entraAdminPrincipalType
      login: entraAdminLogin
      sid: entraAdminObjectId
      tenantId: tenantId
      // Entra-only authentication from creation. No `administratorLogin`: the service generates a
      // CloudSA name that is not a shared account, nor a resource, nor break-glass.
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    // TO VALIDATE: network access. The ledger documentation mentions "Allow Azure services to
    // access this server" for digest storage, and the behaviour with 'Disabled' was not verified.
    // The service default (not set) is kept.
  }
}

// Explicit reinforcement of Entra-only authentication through the dedicated API. The documented
// rule is that the Entra administrator must be set beforehand (it already is, in
// `administrators`).
// Documented consequence: with Entra-only, Elastic Jobs is not supported (see README, ledger
// verification).
resource entraOnly 'Microsoft.Sql/servers/azureADOnlyAuthentications@2025-01-01' = {
  parent: server
  name: 'Default'
  properties: {
    azureADOnlyAuthentication: true
  }
}

resource database 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: databaseSkuName
  }
  properties: {
    isLedgerOn: isLedgerOn
    requestedBackupStorageRedundancy: backupStorageRedundancy
  }
  dependsOn: [
    entraOnly
  ]
}

// Fixed name 'current'. The endpoint has to be a Blob Storage endpoint or the URI of the Azure
// Confidential Ledger.
// Documented prerequisite, outside this Bicep: the server identity needs the Storage Blob Data
// Contributor role on the account (and the secondary replicas, in geo-replication or failover
// groups, need the same role).
// Verification with sp_verify_database_ledger requires ALLOW_SNAPSHOT_ISOLATION to be enabled on
// the database.
resource digestUpload 'Microsoft.Sql/servers/databases/ledgerDigestUploads@2025-01-01' = if (enableLedgerDigestUpload) {
  parent: database
  name: 'current'
  properties: {
    digestStorageEndpoint: digestStorageEndpoint
  }
}

output serverName string = server.name
// Environment suffix instead of an unverified read-only property.
output serverFqdn string = '${server.name}${environment().suffixes.sqlServerHostname}'
output databaseName string = database.name
// TO VALIDATE: depends on the `identity` block above.
output serverPrincipalId string = server.identity.principalId
