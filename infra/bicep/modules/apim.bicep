// APIM instance and an API for the policy-executor, with the validate-azure-ad-token policy.
//
// APIM is the governed entry point for the agent. It is not the access control for the data:
// Microsoft Graph is a public service and no network mechanism applicable to APIM stops whoever
// holds a Graph token from calling Graph directly. What prevents the bypass is that the agent never
// holds the Graph credential, which stays only in the policy-executor. The executor also validates
// the token, because the gateway configuration can change and tokens can be reused.
//
// Verified in Microsoft Learn on 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.apimanagement/2024-05-01/service
// https://learn.microsoft.com/en-us/azure/api-management/validate-azure-ad-token-policy
// https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities
//
// The policy (policies/executor-inbound.xml) uses only attributes transcribed in the research:
//   tenant-id (required), failed-validation-httpcode, failed-validation-error-message,
//   and the client-application-ids and audiences elements.
// The only required attribute is tenant-id. A policy with only that would accept any valid token
// from the tenant, for any audience. That is why the policy requires an audience and a closed list
// of client applications.
//
// TO VALIDATE before applying:
// - the types `Microsoft.ApiManagement/service/apis` and `.../apis/policies` and their properties
//   (displayName, path, protocols, serviceUrl, subscriptionRequired, format, value) are NOT in the
//   verified type table. The resource was requested, so it is written, but all the properties
//   below are TO VALIDATE in the reference;
// - names of the leaf elements (application-id, audience) and order of the children inside
//   validate-azure-ad-token: the research only transcribed the names of the parent elements.
//   Confirm on the policy page ("Set the policy's elements and child elements in the order
//   provided in the policy statement");
// - `authentication-endpoint` is not set: the default appears on the page with a probable typo
//   (login-microsoftonline.com). Do not use that value;
// - the policy only validates the token. It does not block $batch, does not validate the body, and
//   does not handle the 302 to a pre-authenticated URL. The executor applies the allowlist (the API
//   must expose only POST /actions);
// - APIM authentication to the backend (authentication-managed-identity policy, verified in the
//   research but without transcribed attributes): NOT configured. Whoever edits APIM policies can
//   use the managed identity of the service, and APIM does not validate which backend the token is
//   sent to. That is why the data credential is never the APIM identity;
// - network: no virtual network and no private endpoint. APIM is not the defence against bypassing
//   Graph;
// - subscription keys are not identity: `subscriptionRequired` stays false and authentication is
//   only the Entra token.

@description('Azure region.')
param location string

@description('Name prefix, in lower case.')
param namePrefix string

@description('Entra tenant identifier.')
param tenantId string

@description('Publisher email (required, maximum of 100 characters).')
param publisherEmail string

@description('Publisher name (required, maximum of 100 characters).')
param publisherName string

// List of `sku.name` from the reference. No default value: the tier is a cost and network decision
// for Phase A. The nature of the cost per tier was not verified in Learn.
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
@description('APIM tier.')
param skuName string

@description('SKU units. For Consumption it must be 0.')
param skuCapacity int

@description('Base URL (https) of the policy-executor, without a trailing slash.')
param executorBackendUrl string

@description('Audience expected in the tokens for the executor.')
param executorAudience string

@minLength(1)
@description('appId of the client applications authorised to call the gateway (agent identities).')
param executorClientApplicationIds array

@description('Common tags.')
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

// TO VALIDATE: single operation, so that the gateway exposes only POST /actions. It is commented
// out because `Microsoft.ApiManagement/service/apis/operations` is not in the verified table.
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
