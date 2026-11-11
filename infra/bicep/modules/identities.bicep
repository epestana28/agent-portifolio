// User-assigned managed identities, one per privilege boundary.
//
// Design rules:
// - One identity per service. Never reuse the same identity between services with different scopes.
// - The identity of the control-api is NEVER assigned to the agent runtime. This module does not
//   create the identity of the agent runtime: that one is created separately, with no access to the
//   control database, the Service Bus or the Key Vault of this application.
// - The identity that assigns the Selected grant on the SharePoint site also stays outside this
//   module and is distinct from that of the policy-executor (see README).
//
// Type and apiVersion verified in Microsoft Learn on 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.managedidentity/2024-11-30/userassignedidentities

@description('Azure region.')
param location string

@description('Name prefix, in lowercase.')
param namePrefix string

@description('Common tags.')
param tags object = {}

resource controlApi 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${namePrefix}-id-control-api'
  location: location
  tags: union(tags, { 'ap-service': 'control' })
}

resource policyExecutor 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${namePrefix}-id-policy-executor'
  location: location
  tags: union(tags, { 'ap-service': 'executor' })
}

resource containmentWorker 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${namePrefix}-id-containment-worker'
  location: location
  tags: union(tags, { 'ap-service': 'worker' })
}

// TO VALIDATE: the read-only properties `principalId` and `clientId` are not in the properties
// table extracted in the research (the table only lists name, location, tags and isolationScope).
// Confirm on the reference page before applying.
output control object = {
  name: controlApi.name
  id: controlApi.id
  principalId: controlApi.properties.principalId
  clientId: controlApi.properties.clientId
}

output executor object = {
  name: policyExecutor.name
  id: policyExecutor.id
  principalId: policyExecutor.properties.principalId
  clientId: policyExecutor.properties.clientId
}

output worker object = {
  name: containmentWorker.name
  id: containmentWorker.id
  principalId: containmentWorker.properties.principalId
  clientId: containmentWorker.properties.clientId
}
