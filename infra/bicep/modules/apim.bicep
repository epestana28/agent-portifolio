// Instância APIM e uma API para o policy-executor, com a política validate-azure-ad-token.
//
// O APIM é a entrada governada do agente. Não é o controlo de acesso aos dados: o Microsoft Graph é
// um serviço público e nenhum mecanismo de rede aplicável ao APIM impede quem tem um token Graph de
// chamar o Graph directamente. O que impede o contorno é o agente nunca possuir a credencial Graph,
// que fica só no policy-executor. O executor também valida o token, porque a configuração do
// gateway pode mudar e os tokens podem ser reutilizados.
//
// Verificado em Microsoft Learn a 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.apimanagement/2024-05-01/service
// https://learn.microsoft.com/en-us/azure/api-management/validate-azure-ad-token-policy
// https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities
//
// A política (policies/executor-inbound.xml) usa apenas atributos transcritos na pesquisa:
//   tenant-id (obrigatório), failed-validation-httpcode, failed-validation-error-message,
//   e os elementos client-application-ids e audiences.
// O único atributo obrigatório é tenant-id. Uma política só com ele aceitaria qualquer token válido
// do tenant, para qualquer audiência. Por isso a política exige audiência e lista fechada de
// aplicações cliente.
//
// POR VALIDAR antes de aplicar:
// - os tipos `Microsoft.ApiManagement/service/apis` e `.../apis/policies` e as suas propriedades
//   (displayName, path, protocols, serviceUrl, subscriptionRequired, format, value) NÃO constam da
//   tabela de tipos verificada. Foi pedido o recurso, por isso está escrito, mas todas as
//   propriedades abaixo são POR VALIDAR na referência;
// - nomes dos elementos folha (application-id, audience) e ordem dos filhos dentro de
//   validate-azure-ad-token: a pesquisa só transcreveu os nomes dos elementos pai. Confirmar na
//   página da política ("Set the policy's elements and child elements in the order provided in the
//   policy statement");
// - `authentication-endpoint` não é definido: a predefinição aparece na página com um erro
//   tipográfico provável (login-microsoftonline.com). Não usar esse valor;
// - a política só valida o token. Não bloqueia $batch, não valida o corpo, não trata o 302 para URL
//   pré-autenticado. O executor aplica a allowlist (a API deve expor só POST /actions);
// - autenticação do APIM para o backend (política authentication-managed-identity, verificada na
//   pesquisa mas sem atributos transcritos): NÃO configurada. Quem edita políticas APIM pode usar a
//   managed identity do serviço, e o APIM não valida para que backend o token é enviado. Por isso a
//   credencial de dados nunca é a identidade do APIM;
// - rede: sem rede virtual nem private endpoint. O APIM não é a defesa contra contorno do Graph;
// - chaves de subscrição não são identidade: `subscriptionRequired` fica falso e a autenticação é
//   só o token Entra.

@description('Região Azure.')
param location string

@description('Prefixo de nomes, em minúsculas.')
param namePrefix string

@description('Identificador do tenant Entra.')
param tenantId string

@description('Email do publicador (obrigatório, máximo de 100 caracteres).')
param publisherEmail string

@description('Nome do publicador (obrigatório, máximo de 100 caracteres).')
param publisherName string

// Lista de `sku.name` da referência. Sem valor por omissão: o nível é decisão de custo e de rede da
// Fase A. A natureza do custo por nível não foi verificada em Learn.
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
@description('Nível do APIM.')
param skuName string

@description('Unidades do SKU. Para Consumption tem de ser 0.')
param skuCapacity int

@description('URL base (https) do policy-executor, sem barra final.')
param executorBackendUrl string

@description('Audiência esperada nos tokens para o executor.')
param executorAudience string

@minLength(1)
@description('appId das aplicações cliente autorizadas a chamar o gateway (identidades dos agentes).')
param executorClientApplicationIds array

@description('Etiquetas comuns.')
param tags object = {}

var clientIdsXml = join(map(executorClientApplicationIds, id => '<application-id>${id}</application-id>'), '')
var policyTemplate = loadTextContent('../policies/executor-inbound.xml')
var policyXml = replace(replace(replace(policyTemplate, '__TENANT_ID__', tenantId), '__AUDIENCE__', executorAudience), '__CLIENT_APPLICATION_IDS__', clientIdsXml)

resource service 'Microsoft.ApiManagement/service@2024-05-01' = {
  name: '${namePrefix}-apim-${take(uniqueString(resourceGroup().id), 6)}'
  location: location
  tags: tags
  sku: {
    name: skuName
    capacity: skuCapacity
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
  }
}

resource executorApi 'Microsoft.ApiManagement/service/apis@2024-05-01' = {
  parent: service
  name: 'policy-executor'
  properties: {
    displayName: 'Policy Executor'
    path: 'executor'
    protocols: [
      'https'
    ]
    serviceUrl: executorBackendUrl
    subscriptionRequired: false
  }
}

// POR VALIDAR: operação única, para que o gateway exponha só POST /actions. Está em comentário
// porque `Microsoft.ApiManagement/service/apis/operations` não consta da tabela verificada.
// resource actionsOperation 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
//   parent: executorApi
//   name: 'post-actions'
//   properties: {
//     displayName: 'Submit action'
//     method: 'POST'
//     urlTemplate: '/actions'
//   }
// }

resource executorApiPolicy 'Microsoft.ApiManagement/service/apis/policies@2024-05-01' = {
  parent: executorApi
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: policyXml
  }
}

output apimName string = service.name
output executorApiName string = executorApi.name
