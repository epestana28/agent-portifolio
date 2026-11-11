// Service Bus namespace and containment queue.
//
// Verified in Microsoft Learn on 2026-11-11:
// https://learn.microsoft.com/en-us/azure/templates/microsoft.servicebus/2026-01-01/namespaces
// https://learn.microsoft.com/en-us/azure/templates/microsoft.servicebus/2026-01-01/namespaces/queues
// https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection
// https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues
//
// Role of the queue in the design: the outbox lives in the database. If the bus is unavailable, the
// local block remains effective. The queue only carries containment requests. Messages from an
// earlier epoch are discarded by the consumer as "superseded". The worker never reactivates an agent.
//
// What the queue does NOT solve:
// - Delivery is at least once (peek lock). The consumer must be idempotent.
// - Duplicate detection covers SENDING (same MessageId within the window). It does not cover
//   redelivery to the consumer. For detection to work, the producer must use a stable MessageId per
//   outbox message.
// - The dead-letter queue does not clean itself. Someone has to empty it (README and runbooks).

@description('Azure region.')
param location string

@description('Name prefix, in lowercase.')
param namePrefix string

// TO VALIDATE: the `sku.name` values were not extracted from the reference. The tier names come
// from the duplicate detection page (basic, standard, premium). Basic does not support duplicate
// detection, so only Standard and Premium are accepted. Confirm the exact spelling (capitalisation)
// in the reference before applying.
@allowed([
  'Standard'
  'Premium'
])
@description('Namespace tier. Duplicate detection requires Standard or Premium.')
param serviceBusSkuName string

// Documentation default: 10 minutes. Range: 20 seconds to 7 days, ISO 8601 format.
@description('Duplicate detection window (ISO 8601).')
param duplicateDetectionWindow string = 'PT10M'

// Documentation default: 10.
@minValue(1)
@description('Deliveries before the message goes to the dead-letter queue.')
param maxDeliveryCount int = 10

// Documented maximum: 5 minutes. Default: 1 minute.
@description('Message lock duration (ISO 8601).')
param lockDuration string = 'PT1M'

@description('Common tags.')
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
    // No SAS: Entra authentication only. The data roles stay outside the Bicep (README).
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
    // The dead-letter queue always exists and is not declared. This property only handles expiry
    // by TTL: expired messages go to the DLQ instead of disappearing.
    deadLetteringOnMessageExpiration: true
    // No sessions. TO VALIDATE: whether `requiresSession` can be changed after creation, and the
    // availability of sessions per tier, was not verified. Order is not guaranteed: the consumer
    // revalidates status and epoch, it does not depend on order.
    requiresSession: false
  }
}

output namespaceName string = sbNamespace.name
output namespaceId string = sbNamespace.id
output queueName string = containmentQueue.name
