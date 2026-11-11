# Architecture decision log

All decisions are in the proposed state. None has been approved by a human. Approval is part of the phase A gate.

Format of each ADR: Context, Decision, Consequences, Rejected alternatives, Documentary evidence (URL and date 2026-11-11) and Status.

Actual state of the project at this date: phase A is documents written from official documentation and without a tenant, and the fields that depend on the tenant remain "TO BE FILLED IN THE TENANT". Phase B is a local vertical slice in .NET 8, without real credentials, with the Microsoft world simulated. Phases C, D and E have not been executed. The Bicep is written and has never been applied.

| ADR | Title | Status |
|---|---|---|
| [0001](0001-microsoft-first.md) | Microsoft-first | proposed |
| [0002](0002-entra-agent-id-identity-and-adapters.md) | Identity with Entra Agent ID and separate adapters | proposed |
| [0003](0003-opaque-capabilities-no-second-oauth.md) | Opaque capabilities, no second OAuth | proposed |
| [0004](0004-sql-ledger-append-only-and-digests.md) | Append-only SQL Ledger and digests | proposed |
| [0005](0005-database-outbox-and-service-bus.md) | Outbox in the database and Service Bus | proposed |
| [0006](0006-sharepoint-selected-permissions.md) | SharePoint with Selected permissions | proposed |
| [0007](0007-fail-closed-no-positive-cache.md) | Fail closed, no positive cache | proposed |
| [0008](0008-separate-containment-states.md) | Separate containment states | proposed |
| [0009](0009-preview-features.md) | Preview features and documented alternatives | proposed |

Related documents: [threat-model.md](../threat-model.md), [gaps-and-assumptions.md](../gaps-and-assumptions.md), [risks.md](../risks.md) and [backlog.md](../backlog.md).
