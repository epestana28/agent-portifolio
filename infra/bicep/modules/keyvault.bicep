// Key Vault com autorização por RBAC e protecção contra purga.
//
// O Key Vault serve apenas para exceções que exijam segredos. O desenho prefere identidades sem
// segredo. Este módulo não coloca nenhum segredo no cofre e não atribui nenhum papel.
//
// Verificado em Microsoft Learn a 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.keyvault/2026-05-15/vaults
//
// ATENÇÃO, IRREVERSÍVEL: `enablePurgeProtection` não aceita `false` depois de activado, e
// `enableSoftDelete` não volta a `false`. Um cofre apagado mantém o nome reservado durante o
// período de retenção.

@description('Região Azure.')
param location string

@description('Prefixo de nomes, em minúsculas. O nome final tem sufixo derivado do grupo de recursos.')
param namePrefix string

@description('Identificador do tenant Entra.')
param tenantId string

@allowed([
  'standard'
  'premium'
])
@description('Nível do cofre.')
param skuName string = 'standard'

@minValue(7)
@maxValue(90)
@description('Dias de retenção após eliminação (a página indica 7 a 90).')
param softDeleteRetentionInDays int = 90

@description('Etiquetas comuns.')
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
    // POR VALIDAR: a página diz que `accessPolicies` é exigido salvo em createMode recover, mas
    // não diz como isso se conjuga com `enableRbacAuthorization: true`. A lista vazia é a
    // intenção (as políticas de acesso são ignoradas com RBAC). Confirmar que o deploy a aceita.
    accessPolicies: []
    // POR VALIDAR: `publicNetworkAccess` não é definido. Os valores aceites não foram enumerados
    // na pesquisa. Decidir rede privada na Fase A.
  }
}

output vaultName string = vault.name
output vaultId string = vault.id
