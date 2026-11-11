# ADR 0002: Identity with Entra Agent ID and separate adapters

Status: proposed. Source verification date: 2026-11-11.

## Context

Agents can have identities of different kinds: Entra Agent ID, managed identity and service principal. They have different disabling mechanisms, controls and revocation windows. The documentation read does not guarantee an equivalent switch in every case.

The Entra Agent ID model has four objects: blueprint, blueprint principal, agent identity and agent user. Credentials live only in the blueprint and are shared by the child identities. A blueprint can have many identities.

Actual state: the code has `IdentityType` with the three types and `ExecutorObjectId` per agent. The containment adapters for Entra are simulated. None has been tested against a tenant.

## Decision

1. Prefer native individual Entra Agent ID identity where supported. Also confirm the registration in Agent 365: creating the identity does not register the instance.
2. One adapter per identity type (`EntraAgentId`, `ManagedIdentity`, `ServicePrincipal`), behind `IContainmentAdapter`. No adapter pretends to be equivalent to another.
3. Distinguish agent, associated account (agent user), blueprint and blueprint principal. By default, disabling applies to the individual agent. Disabling a blueprint or the tenant requires a distinct visible scope, impact and approval.
4. Map associated accounts and credentials explicitly. Do not assume they disappear with the identity.
5. Each agent has its own `ExecutorObjectId`. The same user-assigned managed identity is not reused across runtimes with incompatible scopes. Grants on the agent itself count as a direct path and lower the coverage to `DirectAccessPossible`.
6. Do not delete identities as the first emergency action. For managed identities, document stopping or isolating the runtime, removing grants, rotating credentials where applicable and the effect of tokens already issued.
7. The caller identity comes from the validated token (`tid` and `oid`), never from `agent_id` in the body. The claims of agent identity tokens are an assumption: TO BE VALIDATED IN THE TENANT.
8. MVP in application mode (the agent identity is the token subject). Agent user and on-behalf-of-user mode are outside the MVP.

## Consequences

- Revoking an agent that uses the three modes (application, on behalf of the user, agent user) requires acting on distinct token subjects. The cascade of the disabling to the agent user is NOT VERIFIED.
- The managed identity has the longest revocation window and the fewest documented controls. The adapter is one of isolation and grant removal, not of disable.
- The exact request body that disables an agent identity is not in an official example. It is probably `{"accountEnabled": false}` (inference) and remains to be validated in phase D.
- Two possible control planes exist (Entra and the Microsoft 365 admin centre of Agent 365). Whether one changes the other is NOT VERIFIED. The report treats them as separate steps.
- More adapters to maintain, with containment tests per type.

## Rejected alternatives

- A single "disable" adapter for all types: the documentation does not guarantee equivalence (managed identity has no documented disable).
- Deleting the identity as the first action: metadata and evidence are lost. Deleted service principals and managed identities keep valid tokens until they expire, and Microsoft documents a 30-day soft delete.
- Disabling the blueprint by default: it affects all child identities.
- Tenant-scoped Conditional Access as the main mechanism: it does not change objects, does not prevent creating identities, does not apply to the token acquisition by the blueprint to create identities, and the agent conditions use Graph `beta` in the examples.
- Using agent user in the MVP: the Agent 365 documentation limits it to the Frontier preview programme.

## Documentary evidence

All verified on 2026-11-11.

- Object model and definitions (blueprint, principal, agent identity, agent user). https://learn.microsoft.com/en-us/entra/agent-id/key-concepts
- Agent identity is a service principal, is single-tenant, credentials are in the blueprint ("Treat each blueprint as a credential boundary"), each identity requires at least one sponsor. https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity
- Disabling an agent identity blocks access and token issuance, and disabling a blueprint blocks the existing ones and prevents new ones. https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
- Per-object disabling without affecting others, and tenant-wide policies that do not prevent the creation of identities. https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities
- `PATCH /servicePrincipals/{id}/microsoft.graph.agentIdentity`, permission `AgentIdentity.EnableDisable.All`, role Agent ID Administrator. https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0
- `accountEnabled` in the `agentIdentity` reference ("no users are able to sign in to this app"), in tension with the administration pages (C10 of the research). https://learn.microsoft.com/en-us/graph/api/resources/agentidentity?view=graph-rest-1.0
- Creating the Entra identity does not register the instance in Agent 365. https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability-concepts
- Managed identity: service principal only, token cache of about 24 hours with no forced refresh, tokens of a deleted identity valid until expiry, 30-day soft delete. https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq
- Managed identities are not covered by Conditional Access. https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity
- Agent user only in Frontier preview programme tenants, in the Agent 365 context. https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity

## Status

Proposed. Real validation is left for phases C and D.
