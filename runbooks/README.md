# Operational runbooks

Procedures for an independent person to follow. Each runbook has an objective, who can run it, prerequisites, numbered steps, how to verify, evidence to record and known limits.

## Actual status

Phase A (validation documents): written from official documentation, without a tenant. Phase B (local vertical slice): implemented in .NET 8, without real credentials, with the Microsoft world simulated. Phases C, D and E: not executed. The Bicep is written and has never been applied. There is no real adapter for Entra or for Graph.

For this reason, none of these runbooks has been executed against real Microsoft services. The Microsoft steps describe the action and point to the official page, and they use only endpoints and cmdlets that the research verified in Microsoft Learn on 2026-11-11. Where the research did not verify them, the text says "confirm in the current documentation before executing". Do not execute those steps without confirming.

The steps that say "inference" are conclusions drawn from verified facts and are not written in the documentation. The ones that say "to be validated" depend on the tenant.

## Index

| Runbook | When to use | Role |
| --- | --- | --- |
| [01. Suspend an agent](01-suspend-agent.md) | Incident with an agent. Immediate local block and follow-up of the external containment. | `IncidentOperator` |
| [02. Revoke a capability](02-revoke-capability.md) | Remove a capability from an agent without suspending it. | `IncidentOperator` or `PolicyAdmin` |
| [03. Reactivate an agent](03-reactivate-agent.md) | After a suspension has been handled. Two distinct `PolicyAdmin`, grants reverified, capabilities reissued. | Two `PolicyAdmin` |
| [04. Break-glass](04-break-glass.md) | The application is unavailable. Direct containment in the Microsoft services, independent of the application. Periodic rehearsal. | Microsoft administrators outside the application |
| [05. Verify the ledger and the digests](05-verify-ledger-and-digests.md) | Periodic verification, after a restore, or suspected tampering. | `Auditor` |
| [06. Restore and failover](06-restore-and-failover.md) | After a database restore or failover. Do not reopen traffic before reconciling. | Operations, `PolicyAdmin`, `Auditor` |
| [07. Retest containment](07-retest-containment.md) | Repeat tests BT-01 to BT-20 locally and know which ones can only be proven in phases C and D. | Engineer |

## How to choose

1. Does the Control API respond? If yes, use runbook 01 to suspend. If not, use runbook 04.
2. Does only one capability have to go, with no suspicion of compromise? Runbook 02.
3. Putting an agent back into service? Runbook 03, always.
4. Was there a restore or a failover? Runbook 06, and within it, 05 before anything else.
5. Suspected tampering with the audit trail? Runbook 05.

## Common rules

- Never record tokens, secrets, prompts or document content in tickets, notes or evidence.
- Suspending is not containing. The agent state and the containment state are separate. `Suspended` never becomes `VerifiedScoped` on its own.
- `VerifiedScoped` is valid only for the explicit scope of each step. Never say "universal access revoked".
- Disabling an identity or removing a grant does not guarantee that already issued tokens are invalidated. The documentation gives no such guarantee, and for agents it does not say what happens.
- A step in an unknown state is unknown, it is not a success.
- The containment worker never reactivates an agent. Reactivation is done only through runbook 03.
- Actions already admitted or sent to Microsoft Graph cannot be cancelled. They are recorded separately, with whatever result they have, including unknown.
- Before executing a step on a Microsoft service marked "confirm in the current documentation", confirm.

## Related documents

- Infrastructure (Bicep, never applied): `../infra/bicep/README.md`
- Source specification: the project specification, section 10 (suspension and revocation), section 11 (ledger) and section 15 (blocking tests)
