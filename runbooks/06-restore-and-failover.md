# 06. Restore and failover

Status of this runbook: written from the design and the official SQL Ledger documentation, without a subscription. Never rehearsed. Test BT-17 is only proved in phases C and D. The documentation of the Azure SQL restore and failover process itself was not read in this research: where the text says "confirm in the current documentation", do not run the step without confirming it.

## Objective

Recover the application after a database restore or a failover without reopening traffic before reconciling, without a suspended agent becoming active again, and with the RPO measured and validated.

Three rules that apply throughout this runbook:

1. Do not reopen traffic before reconciling.
2. A suspended agent cannot become active again because of the restore. Reactivation is only done through runbook 03.
3. The restored database may be behind the real world. Everything the real world did after the restore point has to be discovered, not assumed.

## Who can run this

The operations owner (whoever manages the database and the infrastructure), with a `PolicyAdmin` present to decide on agents and an `Auditor` for the ledger verification. Reopening traffic requires the approval of a `PolicyAdmin` and of the security owner.

## Prerequisites

1. Incident declared, with a ticket and a communications owner.
2. RPO and RTO approved before the incident. This runbook does not set values.
3. Independent records outside the database available: Entra audit and sign-in logs, APIM logs, Service Bus logs, suspension tickets, and the copies, held outside the application, of the agent and grant inventory.
4. Runbooks 01, 03, 04 and 05 to hand.
5. Know how to keep traffic closed: stop the entry at the gateway or the executor, by the approved method. The concrete command depends on the runtime and was not verified: confirm in the current documentation.

## Steps

1. Keep or close traffic. The executor does not admit actions and the control-api does not accept operations until step 11. The worker does not process messages until step 9. Record the UTC time at which traffic was closed.
2. Run the restore or the failover according to the approved procedure. Confirm in the current Azure SQL documentation for the scenario (point-in-time restore, geo-restore, failover group). Record the restore point, the time of the request and the time of completion.
3. Understand the effect on the ledger. The documentation says that each restore gives the database a new creation time and that verification uses the digests of all incarnations. In failover groups, both sides have the same digest path, and the path does not change on failover. If the failover group is deleted or the link is removed, the secondary replica becomes primary with a `RemovedSecondaryReplica` folder in the path. With geo-replication, digests are only emitted for data already replicated to the secondaries.
4. Confirm the digests and the permissions after the restore. The SQL server identity must have the write role on the digest account, and the secondary replicas need the same role. If the restored database is on a different server, the upload configuration has to be redone.
5. Verify the ledger with runbook 05 before anything else. Negative result: stop and treat it as a security incident, because it may be tampering and not a consequence of the restore.
6. Measure the RPO. Get the time of the last commit in the restored database (`MAX(commit_time)` in `sys.database_ledger_transactions`) and compare it with:
   - the time of the last digest in storage (the digest covers data already replicated, so it may be more recent than the restored database if the restore went back in time);
   - the independent records: last executor action in the APIM logs, last message sent to Service Bus, latest Entra and SharePoint events.
   The difference is the loss window. Record the value and compare it with the approved RPO. If it is larger, escalate before continuing.
7. Reconcile the state of the agents. For each agent, compare the state of the restored database with the independent evidence:
   - Any agent that was suspended in the loss window (ticket, containment message, deactivation in Entra) and appears as `Active`: suspend immediately (runbook 01) before any reopening.
   - If in doubt about an agent, suspend. The cost of one suspension too many is lower than that of an improper action.
   - The state of identities and grants in Entra and on the resources was not affected by the restore: what was deactivated or removed there stays that way. Confirm with the inventory held outside the application.
   - A suspended agent cannot go back to `Active` through this procedure. Reactivate only through runbook 03.
8. Handle the capabilities. A database restored back in time may have a `revocationEpoch` lower than the one that existed before the failure, and capabilities revoked in the loss window may appear as valid. Inference to validate in Phase C: treat the capabilities and epochs of that window as unreliable. The safe measure is to suspend the agent (which increments the epoch) and reactivate it through runbook 03, with a new epoch and reissued capabilities. There is no bulk reset endpoint in this documentation.
9. Handle Service Bus. Messages with an epoch different from the current one are discarded as "superseded" and are never executed. Review the dead-letter queue (it does not clear itself): each message in it is decided by a person. Duplicate detection only covers sending within the window, so the consumer has to remain idempotent. Only after the review, turn the worker on.
10. Reconcile external actions. Actions that the executor admitted and sent to Microsoft Graph in the loss window may have taken effect without a record. Search the independent records. Actions with no record but with an effect are recorded as `Unknown` and are reconciled. There is no universal exactly-once for Graph effects.
11. Reconcile the grants: compare the observed grants with the recorded ones. A broad grant (for example `Sites.Read.All`, `Files.Read.All`) is dangerous drift and suspends the agent.
12. Reopen traffic gradually and only after approval: first the control-api and the worker, then the executor. Before admitting agent actions, run the negative containment tests: suspension refuses, unauthorised resource fails, old capability refused.
13. Observe for an agreed period and review the ledger, digest, drift and post-suspension action alerts.
14. Close the incident with the report of the real RPO, of the reconciliations and of the manual actions.

## How to verify

1. The ledger verification passed (runbook 05) after the restore, including the digests of all incarnations.
2. No agent that was suspended is `Active`. List compared with the independent evidence.
3. The measured RPO is within the approved one, or the difference is recorded with an acceptance decision.
4. No agent action was admitted during the closed-traffic window. The audit does not show `action.admitted` between the closing time and the reopening time.
5. The dead-letter queue was reviewed.
6. The negative tests in step 12 passed before reopening.

## Evidence to record

- UTC timeline: failure, decision, traffic closed, restore, verification, reconciliation, reopening.
- Restore point, time of the last restored commit, time of the last digest, loss window and comparison with the RPO.
- Result of the ledger verification.
- List of agents compared with the independent evidence, and the agents suspended again.
- External actions with an `Unknown` result and their reconciliation.
- Dead-letter queue messages and the decision on each one.
- Who approved the reopening.

## Known limits

- The Azure SQL restore and failover process, and the real RPO and RTO, were not verified. They can only be rehearsed in Azure (phases C and E).
- Digests only cover replicated data in geographic scenarios and the ledger cannot rebuild what was lost. The ledger proves tampering, it does not recover data and does not guarantee availability.
- The restore does not reset external state in Entra or on the resources. Conversely, it does not undo external containment.
- Digests and retention: with a locked policy, blobs cannot be deleted before the end of the retention, and the storage account cannot be deleted while there is a container with a locked policy. This constrains any plan to rebuild the environment.
- There is no universal exactly-once. External actions accepted before the failure may complete afterwards.
- This recovery is not automatic. The reconciliations depend on independent evidence, and if it does not exist the runbook requires suspending and reviewing by hand.

## Official documentation (verified on 2026-11-11)

- Digests, restore and failover (incarnations, failover groups): https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management
- Ledger verification: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-verification
- Recovering after tampering: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-how-to-recover-after-tampering
- Ledger limits: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits
- Service Bus, dead-letter: https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues
- Service Bus, duplicate detection: https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection
