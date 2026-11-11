// Identidades geridas atribuídas ao utilizador (user-assigned), uma por fronteira de privilégio.
//
// Regras de desenho:
// - Uma identidade por serviço. Nunca reutilizar a mesma identidade entre serviços com âmbitos diferentes.
// - A identidade da control-api NUNCA é atribuída ao runtime do agente. Este módulo não cria
//   a identidade do runtime do agente: essa é criada à parte, sem qualquer acesso à base de dados
//   de controlo, ao Service Bus ou ao Key Vault desta aplicação.
// - A identidade que atribui o grant Selected no site SharePoint também fica fora deste módulo
//   e é distinta da do policy-executor (ver README).
//
// Tipo e apiVersion verificados em Microsoft Learn a 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.managedidentity/2024-11-30/userassignedidentities

@description('Região Azure.')
param location string

@description('Prefixo de nomes, em minúsculas.')
param namePrefix string

@description('Etiquetas comuns.')
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

// POR VALIDAR: as propriedades só de leitura `principalId` e `clientId` não estão na tabela de
// propriedades extraída na pesquisa (a tabela só lista name, location, tags e isolationScope).
// Confirmar na página de referência antes de aplicar.
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
