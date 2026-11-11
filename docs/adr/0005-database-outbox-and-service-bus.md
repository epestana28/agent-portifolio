# ADR 0005: Database outbox and Service Bus

Status: proposed. Source verification date: 2026-11-11.

## Context

A suspension has to change the state and, in the end, trigger external actions (disable identity, remove grants). If the database and the message transport are written separately, a failure between the two loses the external containment or creates a message with no state to justify it.

Actual state: the local slice writes the `ContainmentOperation`, the outbox message and the audit event in the same SQL transaction. The `ContainmentWorker` reads the outbox from the database. There is no real Service Bus. Service Bus is the target transport and is a design assumption, not something implemented (assumption P-18).

## Decision

1. Transactional outbox pattern: the containment message is written in the same transaction that changes the agent state and increments the `revocation_epoch`. There is no dual write.
2. Each message carries the tenant, the `SubjectId` (the identifier of the `ContainmentOperation`) and the epoch in which it was born. The agent is obtained from the operation.
3. The consumer discards a message from an epoch earlier than the current one as `superseded` (event `containment.superseded`). The worker never reactivates an agent. Only the reactivation approved by a second PolicyAdmin does so.
4. Idempotent consumers. Delivery is at least once, so a repeated step cannot change the result or duplicate ledger events.
5. In production, the transport is Azure Service Bus. Service Bus deduplication only acts on send, in a limited window (default 10 minutes). It does not protect against redelivery to the consumer.
6. Dead-lettered messages raise an alert and enter a handling procedure. The dead-letter queue does not clean itself.
7. Out-of-order messages are expected. Ordering is not guaranteed by the design. The epoch and the state in SQL decide, not the position in the queue.
8. No external step counts as done without a verification probe (ADR 0008).

## Consequences

- There is a delay between the commit and the send. The local block does not depend on it: the `Suspended` state and the new epoch already refuse admissions at commit time.
- A process is needed that reads the outbox and sends it, with handling of failures and resends. That process is custom code to be tested.
- The number of sessions and the support for deduplication per Service Bus tier are NOT VERIFIED. The choice of tier remains to be validated.
- In phase B only the local logic is proved. Behaviour with a real Service Bus (redelivery, dead-letter, out of order) is left for phases C and D.
- The outbox table grows. An archiving policy is needed that does not touch the ledger (ADR 0004).

## Rejected alternatives

- Dual write (database and bus without an outbox): a failure between the two writes loses or invents containment.
- Bus only, with no state in SQL: the admission decision needs the authoritative state, and a lost message leaves no trace.
- Relying on Service Bus deduplication: it only covers sending and a short window.
- Relying on message ordering: the design has to withstand out-of-order and duplicate messages.
- Automatic reactivation by the worker when the containment message fails: contradicts the rule that reactivation requires human approval.

## Documentary evidence

All verified on 2026-11-11. The Service Bus pages were read from search excerpts.

- At-least-once delivery and deduplication: duplicate detection acts on send, with a 10-minute window by default. https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection
- Dead-letter queue: messages are not deleted automatically. https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues
- Receive modes and message settlement (peek-lock). https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-transfers-locks-settlement
- Sessions and ordering: support per tier remains NOT VERIFIED. https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sessions
- The outbox pattern itself is a design inference, it is not on a Microsoft page that was read.

## Status

Proposed. Confirmation of the target transport is assumption P-14 in phase A.
