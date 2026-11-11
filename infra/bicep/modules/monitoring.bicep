// Log Analytics and Application Insights.
//
// Monitor and Application Insights do not replace the ledger. They serve for operational correlation.
//
// Verified in Microsoft Learn on 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.operationalinsights/2026-03-01/workspaces
// https://learn.microsoft.com/en-us/azure/templates/microsoft.insights/2020-02-02/components
// Nature of the cost: https://learn.microsoft.com/en-us/azure/azure-monitor/fundamentals/cost-usage

@description('Azure region.')
param location string

@description('Name prefix, in lowercase.')
param namePrefix string

// TO VALIDATE: "Allowed values are per pricing plan." The page does not enumerate the values for the
// PerGB2018 plan. That is why there is no default value: decide and confirm before applying.
@description('Retention in days of the workspace logs.')
param retentionInDays int

// TO VALIDATE: the semantics of "no limit" for `dailyQuotaGb` were not read. Here, 0 means
// "do not set a daily limit" and the property is omitted.
@description('Daily ingestion limit in GB. 0 omits the property.')
param dailyQuotaGb int = 0

@description('Common tags.')
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

// Note: the property names of this type start with a capital letter (Application_Type,
// WorkspaceResourceId, IngestionMode). That is how they are in the reference.
// TO VALIDATE: `DisableLocalAuth` is not set. Turning off local authentication requires assigning
// the metrics publisher role to each identity, which is outside this Bicep.
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
