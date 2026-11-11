# ADR 0009: Preview features and documented alternatives

Status: proposed. Source verification date: 2026-11-11.

## Context

Several features that the design depends on are in preview, or have no clear statement of general availability. Preview pages have no SLA and can change without notice. A security control that depends on a beta API can disappear or change behaviour.

Actual state: no real Microsoft service was called. This ADR records only what the documentary research of phase A found. When a feature shows neither a preview marker nor a GA sentence, the status is NOT VERIFIED, and not "GA".

## Decision

1. No mandatory MVP control depends on a preview feature without a documented and tested alternative.
2. Preview features only enter as an optional add-on, flagged in the reports, never as the only containment mechanism.
3. Each feature in the table has a stable alternative or a recorded gap.
4. Before phase E, repeat this verification. A feature that changes status calls for a review of this ADR.

| Feature | Status according to the research | Documented alternative |
|---|---|---|
| ID Protection for agents | Preview | Own monitoring and reconciliation, with no automatic blocking by risk |
| Blueprint manifest editor | Preview | Creation by Graph and a documented manual procedure |
| Agent lifecycle policies | Preview (read by excerpt) | Own periodic review and `Retired` state |
| CA condition "Agent execution environments" | Preview | Control in the gateway and in the executor, without this condition |
| Agent user (Agentic-User) | Preview, Frontier programme | Application mode, which is the MVP one (ADR 0002) |
| CA for agents and sign-in logs via Graph beta | Beta in the examples | Control in the gateway, without depending on CA for containment |
| Disable via `isDisabled` (beta) | Beta | `accountEnabled` in v1.0 on the `PATCH` of `agentIdentity`, body to be validated |
| Remove permissions by list or item | Beta only | `DELETE` at site level in v1.0, or consent revocation |
| RBAC for Applications (Exchange) | GA NOT VERIFIED | Outside the MVP, which is SharePoint only |
| Selected permissions per level (list, item) | GA NOT VERIFIED | Selected at site level |
| SQL Ledger in Azure SQL Database | No literal GA sentence, no preview marker | Azure Confidential Ledger as an inferred alternative, with the costs listed in ADR 0004 |
| "Unified model API" and Foundry gateway in APIM | Preview | APIM with standard policies, without these functions |
| "Disable in all tenants" | Endpoint NOT VERIFIED | Disabling per tenant, in each tenant |
| Agent 365 observability SDK | Deprecated, in favour of the Distro | Use the Distro, or OpenTelemetry directly |
| Agent 365 | GA since 2026-05-01 (Commercial) | Not applicable |
| Agent ID | "Available for all customers", literal GA NOT VERIFIED | APIs in v1.0, risk R-01 remains open |

## Consequences

- The product may be left without some containment capability when the alternative is weaker. The report records the limitation (`StandingLimitation`) instead of hiding it.
- Behaviour changes in preview can break adapters. The adapters stay behind `IContainmentAdapter`, which isolates the impact.
- The dependency on the beta API for removing grants by list or item limits fine-grained revocation. The revocation granularity supported in v1.0 is that of the site.
- The cost of maintaining alternatives is accepted. Risk R-01 (dependency on preview) stays open until phase E.
- Nothing in this ADR authorises claiming GA without a source. The phrase "GA NOT VERIFIED" is to be kept.

## Rejected alternatives

- Using the preview features as the main mechanism: no SLA and no guarantee of stability.
- Waiting for everything to be in GA before starting: it would block the project. Phases A and B do not depend on them.
- Assuming GA when the page shows no preview: the absence of a marker is not proof.
- Ignoring the topic and treating the status as an implementation detail: the security risk of a preview control belongs to the product.

## Documentary evidence

All verified on 2026-11-11. Many were read only by search excerpt, and that fact is in the notes of the gap list.

- Agent ID, scope and availability. https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id
- Agent 365 in GA since 2026-05-01 (Commercial). https://learn.microsoft.com/en-us/microsoft-agent-365/overview
- Conditional Access for agents, with conditions in preview and examples in Graph beta. https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id
- ID Protection for agents, in preview. https://learn.microsoft.com/en-us/entra/id-protection/concept-workload-identity-risk
- Disabling and `accountEnabled` in v1.0, with tension between pages. https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0
- Removal of site permissions in v1.0 and by list or item in beta. https://learn.microsoft.com/en-us/graph/api/site-delete-permission?view=graph-rest-1.0
- Agent user in the Frontier programme. https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity
- APIM AI gateway and features in preview. https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities
- Agent 365 observability SDK deprecated. https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability-concepts
- SQL Ledger: pages with neither a preview marker nor a GA sentence. https://learn.microsoft.com/en-us/azure/azure-sql/database/ledger-landing
- Exchange RBAC for Applications. https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac

## Status

Proposed. Re-verify before each phase gate.
