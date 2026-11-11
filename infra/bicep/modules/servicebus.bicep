// Namespace Service Bus e fila de contenção.
//
// Verificado em Microsoft Learn a 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.servicebus/2026-01-01/namespaces
// https://learn.microsoft.com/en-us/azure/templates/microsoft.servicebus/2026-01-01/namespaces/queues
// https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection
// https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues
//
// Papel da fila no desenho: a outbox vive na base de dados. Se o bus estiver indisponível, o
// bloqueio local continua eficaz. A fila só transporta pedidos de contenção. Mensagens de epoch
// anterior são descartadas pelo consumidor como "superseded". O worker nunca reactiva um agente.
//
// O que a fila NÃO resolve:
// - A entrega é pelo menos uma vez (peek lock). O consumidor tem de ser idempotente.
// - A detecção de duplicados cobre o ENVIO (mesmo MessageId dentro da janela). Não cobre a reentrega
//   ao consumidor. Para a detecção funcionar, o produtor tem de usar um MessageId estável por
//   mensagem de outbox.
// - A dead-letter queue não se limpa sozinha. Alguém tem de a esvaziar (README e runbooks).

@description('Região Azure.')
param location string

@description('Prefixo de nomes, em minúsculas.')
param namePrefix string

// POR VALIDAR: os valores de `sku.name` não foram extraídos da referência. Os nomes dos níveis
// vêm da página de duplicate detection (basic, standard, premium). Basic não suporta detecção de
// duplicados, por isso só se aceitam Standard e Premium. Confirmar a grafia exacta (maiúsculas)
// na referência antes de aplicar.
@allowed([
  'Standard'
  'Premium'
])
@description('Nível do namespace. Detecção de duplicados exige Standard ou Premium.')
param serviceBusSkuName string

// Valor por omissão da documentação: 10 minutos. Intervalo: 20 segundos a 7 dias, formato ISO 8601.
@description('Janela de detecção de duplicados (ISO 8601).')
param duplicateDetectionWindow string = 'PT10M'

// Valor por omissão da documentação: 10.
@minValue(1)
@description('Entregas antes de a mensagem ir para a dead-letter queue.')
param maxDeliveryCount int = 10

// Máximo documentado: 5 minutos. Por omissão: 1 minuto.
@description('Duração do bloqueio de mensagem (ISO 8601).')
param lockDuration string = 'PT1M'

@description('Etiquetas comuns.')
param tags object = {}

resource sbNamespace 'Microsoft.ServiceBus/namespaces@2026-01-01' = {
  name: '${namePrefix}-sb-${take(uniqueString(resourceGroup().id), 6)}'
  location: location
  tags: tags
  sku: {
    name: serviceBusSkuName
    tier: serviceBusSkuName
  }
  properties: {
    // Sem SAS: só autenticação Entra. Os papéis de dados ficam fora do Bicep (README).
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource containmentQueue 'Microsoft.ServiceBus/namespaces/queues@2026-01-01' = {
  parent: sbNamespace
  name: 'containment'
  properties: {
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: duplicateDetectionWindow
    maxDeliveryCount: maxDeliveryCount
    lockDuration: lockDuration
    // A dead-letter queue existe sempre e não se declara. Esta propriedade trata só a expiração
    // por TTL: mensagens expiradas vão para a DLQ em vez de desaparecerem.
    deadLetteringOnMessageExpiration: true
    // Sem sessões. POR VALIDAR: se `requiresSession` pode ser alterado depois da criação, e a
    // disponibilidade de sessões por nível, não foi verificado. Ordem não é garantida: o consumidor
    // revalida estado e epoch, não depende da ordem.
    requiresSession: false
  }
}

output namespaceName string = sbNamespace.name
output namespaceId string = sbNamespace.id
output queueName string = containmentQueue.name
