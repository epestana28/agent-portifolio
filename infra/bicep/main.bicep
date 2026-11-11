// Agent Portifolio: ambiente Azure de TESTE (Fase C).
//
// ESTE BICEP NUNCA FOI APLICADO. Não existe subscrição. A sintaxe não foi validada por compilador.
// Não aplicar antes de aprovados os gates da Fase A (âmbito, licenças e permissões) e de decididas
// as escolhas irreversíveis listadas no README (isLedgerOn, retenção dos digests, protecção contra
// purga do Key Vault, autenticação só Entra no SQL).
//
// Âmbito: grupo de recursos. O grupo de recursos é criado fora deste ficheiro.
//
// Tudo o que está marcado `POR VALIDAR` não foi verificado em Microsoft Learn na pesquisa de
// 2026-11-11 (research/03). Não há valores inventados: o que não se sabe fica em comentário ou em
// parâmetro sem valor por omissão.
//
// Não há segredos neste ficheiro nem nos módulos. Os parâmetros que seguem não têm valor por
// omissão quando a escolha tem consequências de custo, segurança ou irreversibilidade.

targetScope = 'resourceGroup'

// ---------- Geral ----------

@minLength(3)
@maxLength(9)
@description('Prefixo de nomes: só letras minúsculas e dígitos, no máximo 9 caracteres (limite de 24 do nome da conta de armazenamento).')
param namePrefix string

@description('Região Azure. Por omissão, a do grupo de recursos.')
param location string = resourceGroup().location

@description('Identificador do tenant Entra.')
param tenantId string

@minLength(1)
@description('Object IDs dos administradores da plataforma. O primeiro é o administrador Entra do servidor SQL e deve ser um grupo. Os papéis dos restantes atribuem-se à mão (README).')
param adminObjectIds array

@description('Nome (login) do administrador Entra do SQL, tal como aparece no directório.')
param sqlAdminLogin string

@allowed([
  'Application'
  'Group'
  'User'
])
@description('Tipo do principal administrador do SQL.')
param sqlAdminPrincipalType string

@description('Etiquetas comuns.')
param tags object = {}

// ---------- Aplicações ----------

@description('Imagem do contentor (única, com os três serviços). Referência sem segredos.')
param containerImage string

@description('Servidor do registo de contentores. Vazio se não for necessária identidade para puxar a imagem.')
param registryServer string = ''

@description('Porta em que a imagem escuta.')
param targetPort int = 8080

@description('Audiência esperada pela control-api (por exemplo o URI do ID da aplicação).')
param controlApiAudience string

@description('Audiência esperada pelo policy-executor.')
param executorAudience string

@description('Modo dos adaptadores (Adapters__Mode). O código aceita só `Simulation` e qualquer outro valor impede o arranque. Não existe adaptador real para Entra ou Graph: não usar um valor que sugira o contrário.')
param adaptersMode string

// ---------- Base de dados e digests ----------

@description('Nome do SKU da base de dados. Sem valor por omissão (POR VALIDAR na referência).')
param databaseSkuName string

@description('Base de dados ledger (todas as tabelas são ledger). IRREVERSÍVEL.')
param isLedgerOn bool

@allowed([
  'Geo'
  'GeoZone'
  'Local'
  'Zone'
])
@description('Redundância do armazenamento de cópias de segurança.')
param backupStorageRedundancy string = 'Geo'

@allowed([
  'Standard_GRS'
  'Standard_GZRS'
  'Standard_RAGRS'
  'Standard_RAGZRS'
])
@description('Redundância da conta de digests. LRS não é suportado para digests automáticos.')
param storageSkuName string

@minValue(1)
@maxValue(146000)
@description('Retenção, em dias, da política de imutabilidade dos digests, antes do lock.')
param digestImmutabilityDays int

@description('Liga o upload automático de digests. Deixar falso no primeiro deploy: o papel de escrita do servidor SQL na conta é manual (README).')
param enableLedgerDigestUpload bool = false

// ---------- Service Bus, monitorização, Key Vault ----------

@allowed([
  'Standard'
  'Premium'
])
@description('Nível do Service Bus. Detecção de duplicados exige Standard ou Premium.')
param serviceBusSkuName string

@description('Retenção dos registos do Log Analytics, em dias. Sem valor por omissão.')
param logRetentionInDays int

@description('Limite diário de ingestão do Log Analytics em GB. 0 não define limite.')
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
@description('Nível do APIM. Sem valor por omissão.')
param apimSkuName string

@description('Unidades do SKU do APIM. Para Consumption tem de ser 0.')
param apimSkuCapacity int

@description('Email do publicador do APIM.')
param apimPublisherEmail string

@description('Nome do publicador do APIM.')
param apimPublisherName string

@minLength(1)
@description('appId das aplicações cliente autorizadas no gateway (identidades dos agentes).')
param executorClientApplicationIds array

// ---------- Variáveis ----------

// O código usa o emissor declarado nos metadados desta autoridade (Auth__Authority). A versão do
// emissor dos tokens reais é POR VALIDAR no tenant. `loginEndpoint` do ambiente termina em barra.
var authAuthority = '${environment().authentication.loginEndpoint}${tenantId}/v2.0'

// ---------- Módulos, pela ordem de dependência ----------

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

// ---------- Saídas (sem segredos) ----------
// Servem para os passos manuais do README: atribuições de papéis e utilizadores T-SQL.

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
