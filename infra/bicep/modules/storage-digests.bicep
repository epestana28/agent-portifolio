// Conta de armazenamento e contentor de digests da ledger do Azure SQL.
//
// Destino base dos digests: Blob com política de imutabilidade (WORM) ao nível do contentor.
// O Azure Confidential Ledger fica como opção avaliada na Fase A e não é criado aqui (o tipo e a
// apiVersion Bicep dele não foram verificados).
//
// Verificado em Microsoft Learn a 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.storage/2026-09-01/storageaccounts
// https://learn.microsoft.com/en-us/azure/templates/microsoft.storage/2026-09-01/storageaccounts/blobservices/containers
// https://learn.microsoft.com/en-us/azure/templates/microsoft.storage/2026-09-01/storageaccounts/blobservices/containers/immutabilitypolicies
// https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management
// https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-container-level-worm-policies
//
// POLÍTICA DE IMUTABILIDADE: NÃO BLOQUEADA, DE PROPÓSITO
// -------------------------------------------------------
// A política criada aqui fica no estado desbloqueado. A documentação diz que uma política
// desbloqueada protege contra eliminação e substituição, mas pode ser encurtada ou apagada, e
// recomenda-a só para testes de curta duração. O bloqueio (lock) é um passo manual, feito depois
// de aprovada a retenção, porque:
// - depois de bloqueada, a política não se apaga e o período só pode aumentar (máximo de cinco
//   aumentos numa política ao nível do contentor);
// - depois de bloqueada, `allowProtectedAppendWrites` já não pode ser alterada;
// - a conta de armazenamento não pode ser eliminada enquanto houver um contentor com política
//   bloqueada e blobs presentes, nem antes de a retenção expirar;
// - a retenção tem de estar decidida (RGPD, custo, residência) antes de se tornar irreversível.
// O lock não está confirmado como declarável em Bicep (NÃO VERIFICADO na pesquisa). O comando
// documentado é `az storage container immutability-policy lock ... --if-match $etag`. Ver README.

@description('Região Azure.')
param location string

@description('Prefixo de nomes, em minúsculas e sem hífenes, com no máximo 9 caracteres (limite de 24 do nome da conta).')
param namePrefix string

// A documentação da ledger indica que o upload automático de digests não suporta contas LRS.
// Não existe valor por omissão: a redundância é uma decisão da Fase A.
@allowed([
  'Standard_GRS'
  'Standard_GZRS'
  'Standard_RAGRS'
  'Standard_RAGZRS'
])
@description('Redundância da conta. LRS não é suportado para digests automáticos.')
param storageSkuName string

// Intervalo documentado: 1 a 146000 dias. Sem valor por omissão: a documentação avisa que o valor
// de 1 dia do quickstart não serve para produção, e a decisão é da Fase A.
@minValue(1)
@maxValue(146000)
@description('Retenção, em dias, da política de imutabilidade do contentor (antes do lock).')
param immutabilityPeriodInDays int

@description('Etiquetas comuns.')
param tags object = {}

// Nome fixo documentado: o servidor lógico do Azure SQL usa este contentor.
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
    // POR VALIDAR: a documentação diz que o servidor SQL escreve os digests com RBAC (papel
    // Storage Blob Data Contributor). Não foi verificado que o upload funciona com chaves
    // partilhadas desligadas. Se falhar na Fase C, esta linha é a primeira a rever.
    allowSharedKeyAccess: false
    // POR VALIDAR: `publicNetworkAccess` e firewall da conta não são definidos. O comportamento
    // do upload de digests com rede restrita não foi verificado.
  }
}

// POR VALIDAR: a documentação diz que o servidor "creates a new container" e que a política se
// configura "after provisioning". Se o contentor pode ser criado aqui, com política, antes do
// primeiro upload, não está confirmado. Se o upload falhar por o contentor já existir, remover
// estes dois recursos e criar a política depois do primeiro upload, como no quickstart.
//
// O tipo `blobServices` não consta da tabela de tipos verificados. Os nomes completos abaixo
// evitam declará-lo e usam apenas os tipos verificados.
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
    // Necessário para os digests: são append blobs.
    allowProtectedAppendWrites: true
    // Sem `state` e sem lock: ver o cabeçalho deste ficheiro.
  }
}

output accountName string = account.name
output accountId string = account.id
output containerName string = containerName
// Ponto final de blobs construído a partir do sufixo do ambiente. É o valor que o
// `ledgerDigestUploads` espera em `digestStorageEndpoint`.
output blobEndpoint string = 'https://${account.name}.blob.${environment().suffixes.storage}'
