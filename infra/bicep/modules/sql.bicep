// Servidor Azure SQL com autenticação só Entra, base de dados e configuração do upload de digests.
//
// Verificado em Microsoft Learn a 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers
// https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/azureadonlyauthentications
// https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/databases
// https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/databases/ledgerdigestuploads
// https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication
//
// O que este módulo NÃO faz, de propósito:
// - Não cria utilizadores da base de dados. `CREATE USER ... FROM EXTERNAL PROVIDER` é T-SQL, não é
//   declarável em Bicep. Fica como passo pós-deploy (README).
// - Não cria o papel que dá ao servidor SQL escrita na conta de digests (Storage Blob Data
//   Contributor). Fica como passo manual (README). Por isso o upload de digests está desligado por
//   omissão (`enableLedgerDigestUpload`) e liga-se num segundo deploy.
// - Não cria regras de firewall nem private endpoint (tipos não verificados). Sem uma delas, nenhum
//   cliente consegue ligar-se pela rede pública. Decisão de rede na Fase A.

@description('Região Azure.')
param location string

@description('Prefixo de nomes, em minúsculas.')
param namePrefix string

@description('Identificador do tenant Entra.')
param tenantId string

@description('Object ID do administrador Entra do servidor. Recomenda-se um grupo.')
param entraAdminObjectId string

@description('Nome (login) do administrador Entra, tal como aparece no directório.')
param entraAdminLogin string

@allowed([
  'Application'
  'Group'
  'User'
])
@description('Tipo do principal administrador.')
param entraAdminPrincipalType string

@description('Nome da base de dados.')
param databaseName string = 'agentportifolio'

// POR VALIDAR: os valores de `sku.name` não estão enumerados na referência. O exemplo T-SQL da
// documentação usa o objectivo de serviço 'GP_Gen5_2', mas isso não prova que seja o `sku.name`
// aceite pelo ARM. Sem valor por omissão.
@description('Nome do SKU da base de dados.')
param databaseSkuName string

// DECISÃO IRREVERSÍVEL. Com `true`, TODAS as tabelas da base passam a ser tabelas ledger, incluindo
// as operacionais e as de migrações (actualizáveis por omissão, com tabela de histórico que não se
// pode apagar). Com `false`, escolhe-se tabela a tabela com WITH (LEDGER = ON (APPEND_ONLY = ON)).
// Nenhuma das duas opções se reverte. Sem valor por omissão: decidir na Fase A.
@description('Base de dados ledger (todas as tabelas são ledger). Não pode ser alterado depois.')
param isLedgerOn bool

@allowed([
  'Geo'
  'GeoZone'
  'Local'
  'Zone'
])
@description('Redundância do armazenamento de cópias de segurança. Decisão ligada ao RPO.')
param backupStorageRedundancy string = 'Geo'

@description('Liga o upload automático de digests. Só depois de atribuído o papel de escrita à identidade do servidor.')
param enableLedgerDigestUpload bool = false

@description('Ponto final de blobs da conta de digests (ou URI do Confidential Ledger).')
param digestStorageEndpoint string

@description('Etiquetas comuns.')
param tags object = {}

var serverName = '${namePrefix}-sql-${take(uniqueString(resourceGroup().id), 6)}'

resource server 'Microsoft.Sql/servers@2025-01-01' = {
  name: serverName
  location: location
  tags: tags
  // POR VALIDAR: o bloco `identity` do servidor não consta da tabela extraída. A documentação dos
  // digests fala em "logical server (System Identity)" como identidade que escreve na conta de
  // armazenamento. Se uma identidade user-assigned serve para o upload: NÃO VERIFICADO.
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    administrators: {
      // POR VALIDAR: a referência pode exigir `administratorType: 'ActiveDirectory'`. A propriedade
      // não está na lista extraída, por isso não é definida aqui. Confirmar antes de aplicar.
      principalType: entraAdminPrincipalType
      login: entraAdminLogin
      sid: entraAdminObjectId
      tenantId: tenantId
      // Autenticação só Entra desde a criação. Sem `administratorLogin`: o serviço gera um nome
      // CloudSA que não é conta partilhada, nem recurso, nem break-glass.
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    // POR VALIDAR: acesso de rede. A documentação da ledger refere "Allow Azure services to access
    // this server" para o armazenamento de digests, e o comportamento com 'Disabled' não foi
    // verificado. Fica o valor por omissão do serviço (não definido).
  }
}

// Reforço explícito da autenticação só Entra pela API dedicada. A regra documentada é que o
// administrador Entra tem de estar definido antes (já está, em `administrators`).
// Consequência documentada: com Entra-only, Elastic Jobs não é suportado (ver README, verificação
// da ledger).
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

// Nome fixo 'current'. O ponto final tem de ser um endpoint de Blob Storage ou o URI do Azure
// Confidential Ledger.
// Pré-requisito documentado, fora deste Bicep: a identidade do servidor precisa do papel Storage
// Blob Data Contributor na conta (e as réplicas secundárias, em geo-replicação ou grupos de
// failover, precisam do mesmo papel).
// A verificação com sp_verify_database_ledger exige ALLOW_SNAPSHOT_ISOLATION activo na base.
resource digestUpload 'Microsoft.Sql/servers/databases/ledgerDigestUploads@2025-01-01' = if (enableLedgerDigestUpload) {
  parent: database
  name: 'current'
  properties: {
    digestStorageEndpoint: digestStorageEndpoint
  }
}

output serverName string = server.name
// Sufixo do ambiente em vez de propriedade só de leitura não verificada.
output serverFqdn string = '${server.name}${environment().suffixes.sqlServerHostname}'
output databaseName string = database.name
// POR VALIDAR: depende do bloco `identity` acima.
output serverPrincipalId string = server.identity.principalId
