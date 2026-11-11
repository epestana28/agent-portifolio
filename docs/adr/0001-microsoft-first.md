# ADR 0001: Microsoft-first

Status: proposed. Source verification date: 2026-11-11.

## Context

The project builds a control and evidence layer for agents that run in Azure with access to Microsoft 365. The specification requires reusing existing Microsoft services before writing equivalent components (section 3). It is not permitted to create a cryptocurrency, a custom blockchain, a custom OAuth issuer or a parallel identity system.

Actual state: phase A was written from official documentation and without a tenant. Phase B (local slice) is implemented with the Microsoft world simulated. Phases C, D and E have not been executed, and there is no real adapter for Entra or for Graph.

## Decision

Each need uses the corresponding Microsoft service first. Custom code is limited to what Microsoft does not cover.

| Need | Microsoft service | Custom code allowed |
|---|---|---|
| Agent identity | Entra Agent ID, where supported | Adapter and correlation |
| Existing workloads | Managed identities and service principals | Separate adapters |
| Governed entry | Azure API Management and Entra policies | Validation of capabilities and resources in the executor |
| SharePoint permissions | Selected permissions | Orchestration of grants and verification |
| Verifiable history | Azure SQL Database Ledger | Schema and domain events |
| Independent digests | Immutable Blob, with Confidential Ledger as an option | Scheduling and complementary verification |
| Secrets | Key Vault | No custom vault |
| Observability | Azure Monitor and OpenTelemetry, Agent 365 when licensed | Instrumentation and correlation |
| Infrastructure | Bicep | Product modules |

The product builds the wallet, the deterministic authorisation, the containment states and the domain evidence. It does not build identity, ledger, gateway, vault or token issuer.

Agent 365, Defender, Purview and Sentinel are add-ons subject to support and licensing. Coverage of any given agent, or synchronous blocking of any given action, is not assumed.

## Consequences

- The project depends on Microsoft services and their licences. The contradiction about the Conditional Access licence for agents remains unresolved in the tenant (risk R-04).
- Some relevant features are in preview or without a GA statement. See ADR 0009.
- APIM does not turn the governance of model traffic into fine-grained authorisation of Graph operations. That part is custom code.
- Every claim about Microsoft must carry a source and a date. What is not documented stays marked as NOT VERIFIED and is not softened.
- The product can only be validated in a tenant. Until then, phases C and D remain blocked and the simulated Microsoft world does not count as proof.

## Rejected alternatives

- A custom identity or OAuth issuer: duplicates Entra and creates a second identity system, which the specification forbids.
- A custom hash chain or blockchain for the evidence: see ADR 0004.
- A third-party gateway or proxy in place of APIM: it is not Microsoft-first and does not solve the lack of fine-grained authorisation of Graph operations. Remains to be justified.
- Conditional Access as the only containment mechanism: agent policies do not change objects, do not prevent the creation of identities, and do not apply to access that does not go through Entra token issuance (for example an API key).
- Waiting for a Microsoft product that covers everything: the documentation read does not show a per-agent capability wallet, nor a revocation ledger, nor inspection of `$batch` or of 302 redirects.

## Documentary evidence

All verified on 2026-11-11.

- Agent ID: "available for all Microsoft Entra customers". Literal GA status NOT VERIFIED. https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id
- Agent 365 in GA since 2026-05-01, Commercial segment, per-user licence. https://learn.microsoft.com/en-us/microsoft-agent-365/overview
- The APIM AI gateway "extends API Management's existing API gateway; it's not a separate offering", and governs model traffic and MCP and A2A tools. The absence of capability for Graph semantics is an inference from absence on the page. https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities
- Conditional Access does not apply to API key access, which bypasses Entra token issuance. https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id
- Tenant-scoped Conditional Access policies "doesn't prevent the creation of agent identities". https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities
- Licensing of Agent 365 and Conditional Access for agents, with a contradiction between pages. https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id and https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities

## Status

Proposed. Human approval still to be obtained at the phase A gate.
