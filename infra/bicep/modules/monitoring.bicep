// Log Analytics e Application Insights.
//
// Monitor e Application Insights não substituem a ledger. Servem para correlação operacional.
//
// Verificado em Microsoft Learn a 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.operationalinsights/2026-03-01/workspaces
// https://learn.microsoft.com/en-us/azure/templates/microsoft.insights/2020-02-02/components
// Natureza do custo: https://learn.microsoft.com/en-us/azure/azure-monitor/fundamentals/cost-usage

@description('Região Azure.')
param location string

@description('Prefixo de nomes, em minúsculas.')
param namePrefix string

// POR VALIDAR: "Allowed values are per pricing plan." A página não enumera os valores para o plano
// PerGB2018. Por isso não há valor por omissão: decidir e confirmar antes de aplicar.
@description('Retenção em dias dos registos do workspace.')
param retentionInDays int

// POR VALIDAR: a semântica de "sem limite" para `dailyQuotaGb` não foi lida. Aqui, 0 significa
// "não definir limite diário" e a propriedade é omitida.
@description('Limite diário de ingestão em GB. 0 omite a propriedade.')
param dailyQuotaGb int = 0

@description('Etiquetas comuns.')
param tags object = {}

resource workspace 'Microsoft.OperationalInsights/workspaces@2026-03-01' = {
  name: '${namePrefix}-log'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    workspaceCapping: dailyQuotaGb > 0 ? {
      dailyQuotaGb: dailyQuotaGb
    } : null
  }
}

// Nota: os nomes das propriedades deste tipo têm maiúscula inicial (Application_Type,
// WorkspaceResourceId, IngestionMode). É assim na referência.
// POR VALIDAR: `DisableLocalAuth` não é definido. Desligar a autenticação local obriga a atribuir
// papel de publicação de métricas a cada identidade, o que fica fora deste Bicep.
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-appi'
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

output workspaceName string = workspace.name
output workspaceId string = workspace.id
output appInsightsName string = appInsights.name
output appInsightsId string = appInsights.id
