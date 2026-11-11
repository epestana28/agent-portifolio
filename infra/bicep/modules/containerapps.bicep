// Ambiente Container Apps e as três aplicações: control-api, policy-executor e containment-worker.
//
// Cada aplicação usa a SUA identidade (user-assigned), criada em identities.bicep:
// - control-api: identidade de controlo. Nunca é atribuída ao runtime do agente.
// - policy-executor: detém a credencial de dados. O agente nunca a recebe.
// - containment-worker: corre os adaptadores de contenção. Sem ingress.
//
// Verificado em Microsoft Learn a 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.app/2026-07-01/managedenvironments
// https://learn.microsoft.com/en-us/azure/templates/microsoft.app/2026-07-01/containerapps
// https://learn.microsoft.com/en-us/azure/container-apps/billing
//
// POR VALIDAR (resumo, o detalhe está junto de cada linha):
// - formato do bloco `identity` das container apps (a pesquisa não o extraiu);
// - `template.containers[]` (name, image, env), não extraído na pesquisa;
// - restrição do ingress do executor ao APIM (ver nota no recurso);
// - `listKeys()` do workspace para a configuração de registos;
// - formato exacto da cadeia de ligação SQL com managed identity.

@description('Região Azure.')
param location string

@description('Prefixo de nomes, em minúsculas.')
param namePrefix string

@description('Nome do workspace Log Analytics (criado em monitoring.bicep, no mesmo grupo de recursos).')
param logAnalyticsWorkspaceName string

@description('Imagem do contentor (única, com os três serviços). Sem segredos na referência.')
param containerImage string

@description('Servidor do registo de contentores. Vazio se a imagem for de um registo que não exige identidade.')
param registryServer string = ''

@description('Porta do contentor. Tem de coincidir com a porta em que a imagem escuta.')
param targetPort int = 8080

@description('Identificador do tenant Entra.')
param tenantId string

@description('Autoridade do emissor de tokens, por exemplo o ponto final do tenant.')
param authAuthority string

@description('Audiência esperada pela control-api.')
param controlApiAudience string

@description('Audiência esperada pelo policy-executor.')
param executorAudience string

@description('Modo dos adaptadores (Adapters__Mode). Sem valor por omissão. O código aceita só `Simulation`.')
param adaptersMode string

@description('FQDN do servidor SQL.')
param sqlServerFqdn string

@description('Nome da base de dados.')
param databaseName string

@description('Identidade da control-api: id de recurso e clientId.')
param controlIdentity object

@description('Identidade do policy-executor: id de recurso e clientId.')
param executorIdentity object

@description('Identidade do containment-worker: id de recurso e clientId.')
param workerIdentity object

@description('Etiquetas comuns.')
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
        // `sharedKey` é um valor sensível ("Pass in as a secure parameter"). Aqui é lido do próprio
        // workspace dentro do template e nunca sai como output.
        // POR VALIDAR: `listKeys()` sobre o workspace e `properties.customerId` não foram
        // verificados na pesquisa.
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    // POR VALIDAR: `workloadProfiles` não é definido (valores de `workloadProfileType` não
    // enumerados). Sem perfis, o ambiente usa o plano de consumo.
    // Rede: sem `vnetConfiguration`. Rede privada é decisão da Fase A.
  }
}

// Cadeias de ligação SQL sem palavra-passe, com identidade gerida. Cada serviço liga-se com a
// SUA identidade (clientId próprio). O utilizador da base de dados tem de ser criado em T-SQL com
// CREATE USER ... FROM EXTERNAL PROVIDER (README).
// O código passa a cadeia tal e qual ao Microsoft.Data.SqlClient. POR VALIDAR: a grafia exacta
// (Authentication=Active Directory Managed Identity; User Id=<clientId>) vem do formato habitual do
// driver, não da pesquisa. Confirmar na documentação do driver e ensaiar contra Azure SQL (fase C).
var sqlConnectionControl = 'Server=tcp:${sqlServerFqdn},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${controlIdentity.clientId};Encrypt=True'
var sqlConnectionExecutor = 'Server=tcp:${sqlServerFqdn},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${executorIdentity.clientId};Encrypt=True'
var sqlConnectionWorker = 'Server=tcp:${sqlServerFqdn},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${workerIdentity.clientId};Encrypt=True'

// Variáveis de ambiente comuns aos três serviços.
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
  // As identidades de execução não têm ENABLE LEDGER, que o esquema exige. O esquema aplica-se
  // fora da aplicação, com uma conta administrativa (README). O código, por omissão, tentaria
  // criá-lo no arranque.
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

// O worker não recebe pedidos HTTP e não valida tokens: ContainmentWorker/Program.cs não lê nenhuma
// variável Auth__. Elas são enviadas só porque o contrato comum as lista para os três serviços e
// podem sair sem efeito. O worker lê Worker__PollSeconds (omissão 2 s), que aqui não é definida.
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
  // POR VALIDAR: formato do bloco `identity` (tipo e `userAssignedIdentities`) não extraído.
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
          // POR VALIDAR: sem `resources` (cpu, memória). Dimensionar na Fase C.
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
      // ATENÇÃO, GATE DA FASE C: o ingress está externo e SEM restrição ao APIM. Com este Bicep,
      // o executor é alcançável sem passar pelo gateway, por isso o BT-05 (gateway e backend não
      // podem ser contornados) NÃO fica provado. A mitigação mínima documentada é o executor
      // validar o token ele próprio (defesa em profundidade, feito no código). A restrição de rede
      // ao APIM (ipSecurityRestrictions, rede virtual ou private endpoint) é POR VALIDAR: a
      // propriedade existe (`ipSecurityRestrictions`), mas os seus campos não foram extraídos.
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

// Sem ingress: o worker só consome a outbox e a fila, e fala com os serviços Microsoft através dos
// adaptadores. Uma réplica no máximo: os adaptadores são idempotentes, mas não há razão para
// paralelismo na Fase C.
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
// POR VALIDAR: `configuration.ingress.fqdn` é só de leitura e não está na tabela extraída.
output controlApiFqdn string = controlApi.properties.configuration.ingress.fqdn
output executorFqdn string = policyExecutor.properties.configuration.ingress.fqdn
