// Container Apps environment and the three applications: control-api, policy-executor and
// containment-worker.
//
// Each application uses ITS OWN identity (user-assigned), created in identities.bicep:
// - control-api: control identity. It is never assigned to the agent runtime.
// - policy-executor: holds the data credential. The agent never receives it.
// - containment-worker: runs the containment adapters. No ingress.
//
// Verified in Microsoft Learn on 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.app/2026-07-01/managedenvironments
// https://learn.microsoft.com/en-us/azure/templates/microsoft.app/2026-07-01/containerapps
// https://learn.microsoft.com/en-us/azure/container-apps/billing
//
// TO VALIDATE (summary, the detail is next to each line):
// - format of the `identity` block of the container apps (the research did not extract it);
// - `template.containers[]` (name, image, env), not extracted in the research;
// - restriction of the executor ingress to APIM (see the note on the resource);
// - `listKeys()` of the workspace for the log configuration;
// - exact format of the SQL connection string with managed identity.

@description('Azure region.')
param location string

@description('Name prefix, in lower case.')
param namePrefix string

@description('Name of the Log Analytics workspace (created in monitoring.bicep, in the same resource group).')
param logAnalyticsWorkspaceName string

@description('Container image (a single one, with the three services). No secrets in the reference.')
param containerImage string

@description('Container registry server. Empty if the image comes from a registry that does not require identity.')
param registryServer string = ''

@description('Container port. It must match the port the image listens on.')
param targetPort int = 8080

@description('Entra tenant identifier.')
param tenantId string

@description('Token issuer authority, for example the tenant endpoint.')
param authAuthority string

@description('Audience expected by the control-api.')
param controlApiAudience string

@description('Audience expected by the policy-executor.')
param executorAudience string

@description('Adapters mode (Adapters__Mode). No default value. The code accepts only `Simulation`.')
param adaptersMode string

@description('FQDN of the SQL server.')
param sqlServerFqdn string

@description('Name of the database.')
param databaseName string

@description('Identity of the control-api: resource id and clientId.')
param controlIdentity object

@description('Identity of the policy-executor: resource id and clientId.')
param executorIdentity object

@description('Identity of the containment-worker: resource id and clientId.')
param workerIdentity object

@description('Common tags.')
param tags object = {}

resource workspace 'Microsoft.OperationalInsights/workspaces@2026-03-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource cae 'Microsoft.App/managedEnvironments@2026-07-01' = {
  name: '${namePrefix}-cae'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        // `sharedKey` is a sensitive value ("Pass in as a secure parameter"). Here it is read from
        // the workspace itself inside the template and never leaves as an output.
        // TO VALIDATE: `listKeys()` on the workspace and `properties.customerId` were not verified
        // in the research.
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    // TO VALIDATE: `workloadProfiles` is not set (values of `workloadProfileType` not enumerated).
    // Without profiles, the environment uses the consumption plan.
    // Network: no `vnetConfiguration`. Private networking is a Phase A decision.
  }
}

// SQL connection strings without a password, with managed identity. Each service connects with ITS
// OWN identity (its own clientId). The database user has to be created in T-SQL with
// CREATE USER ... FROM EXTERNAL PROVIDER (README).
// The code passes the string as is to Microsoft.Data.SqlClient. TO VALIDATE: the exact spelling
// (Authentication=Active Directory Managed Identity; User Id=<clientId>) comes from the usual
// driver format, not from the research. Confirm in the driver documentation and rehearse against
// Azure SQL (phase C).
var sqlConnectionControl = 'Server=tcp:${sqlServerFqdn},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${controlIdentity.clientId};Encrypt=True'
var sqlConnectionExecutor = 'Server=tcp:${sqlServerFqdn},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${executorIdentity.clientId};Encrypt=True'
var sqlConnectionWorker = 'Server=tcp:${sqlServerFqdn},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${workerIdentity.clientId};Encrypt=True'

// Environment variables common to the three services.
var commonEnv = [
  {
    name: 'Storage__Provider'
    value: 'SqlServer'
  }
  {
    name: 'Auth__Authority'
    value: authAuthority
  }
  {
    name: 'Auth__AllowedTenants__0'
    value: tenantId
  }
  {
    name: 'Adapters__Mode'
    value: adaptersMode
  }
  // The runtime identities do not have ENABLE LEDGER, which the schema requires. The schema is
  // applied outside the application, with an administrative account (README). By default, the code
  // would try to create it at startup.
  {
    name: 'Storage__EnsureSchema'
    value: 'false'
  }
]

var controlEnv = concat([
  {
    name: 'AP_SERVICE'
    value: 'control'
  }
  {
    name: 'Storage__ConnectionString'
    value: sqlConnectionControl
  }
  {
    name: 'Auth__Audience'
    value: controlApiAudience
  }
], commonEnv)

var executorEnv = concat([
  {
    name: 'AP_SERVICE'
    value: 'executor'
  }
  {
    name: 'Storage__ConnectionString'
    value: sqlConnectionExecutor
  }
  {
    name: 'Auth__Audience'
    value: executorAudience
  }
], commonEnv)

// The worker does not receive HTTP requests and does not validate tokens: ContainmentWorker/Program.cs
// does not read any Auth__ variable. They are sent only because the common contract lists them for
// all three services and they can be dropped without effect. The worker reads Worker__PollSeconds
// (default 2 s), which is not set here.
var workerEnv = concat([
  {
    name: 'AP_SERVICE'
    value: 'worker'
  }
  {
    name: 'Storage__ConnectionString'
    value: sqlConnectionWorker
  }
  {
    name: 'Auth__Audience'
    value: controlApiAudience
  }
], commonEnv)

resource controlApi 'Microsoft.App/containerApps@2026-07-01' = {
  name: '${namePrefix}-control-api'
  location: location
  tags: union(tags, { 'ap-service': 'control' })
  // TO VALIDATE: format of the `identity` block (type and `userAssignedIdentities`) not extracted.
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${controlIdentity.id}': {}
    }
  }
  properties: {
    environmentId: cae.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: targetPort
        allowInsecure: false
      }
      registries: empty(registryServer) ? [] : [
        {
          server: registryServer
          identity: controlIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'control-api'
          image: containerImage
          env: controlEnv
          // TO VALIDATE: no `resources` (cpu, memory). Size in Phase C.
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource policyExecutor 'Microsoft.App/containerApps@2026-07-01' = {
  name: '${namePrefix}-policy-executor'
  location: location
  tags: union(tags, { 'ap-service': 'executor' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${executorIdentity.id}': {}
    }
  }
  properties: {
    environmentId: cae.id
    configuration: {
      activeRevisionsMode: 'Single'
      // WARNING, PHASE C GATE: the ingress is external and WITHOUT restriction to APIM. With this
      // Bicep, the executor is reachable without going through the gateway, so BT-05 (gateway and
      // backend cannot be bypassed) is NOT proved. The documented minimum mitigation is for the
      // executor to validate the token itself (defence in depth, done in the code). Network
      // restriction to APIM (ipSecurityRestrictions, virtual network or private endpoint) is TO
      // VALIDATE: the property exists (`ipSecurityRestrictions`), but its fields were not
      // extracted.
      ingress: {
        external: true
        targetPort: targetPort
        allowInsecure: false
      }
      registries: empty(registryServer) ? [] : [
        {
          server: registryServer
          identity: executorIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'policy-executor'
          image: containerImage
          env: executorEnv
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
}

// No ingress: the worker only consumes the outbox and the queue, and talks to the Microsoft
// services through the adapters. One replica at most: the adapters are idempotent, but there is no
// reason for parallelism in Phase C.
resource containmentWorker 'Microsoft.App/containerApps@2026-07-01' = {
  name: '${namePrefix}-containment-worker'
  location: location
  tags: union(tags, { 'ap-service': 'worker' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workerIdentity.id}': {}
    }
  }
  properties: {
    environmentId: cae.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: empty(registryServer) ? [] : [
        {
          server: registryServer
          identity: workerIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'containment-worker'
          image: containerImage
          env: workerEnv
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output environmentName string = cae.name
output controlApiName string = controlApi.name
output executorName string = policyExecutor.name
output workerName string = containmentWorker.name
// TO VALIDATE: `configuration.ingress.fqdn` is read-only and is not in the extracted table.
output controlApiFqdn string = controlApi.properties.configuration.ingress.fqdn
output executorFqdn string = policyExecutor.properties.configuration.ingress.fqdn
