# Containment coverage matrix

State: draft pending validation in the tenant

Verified against official documentation on 2026-11-11

## 1. Purpose and real state

This document classifies, by type of agent or identity, what the product can contain and with what proof. It answers section 20 of the specification ("coverage classification by agent type").

The classifications below are ceilings. They indicate the maximum the product can honestly assign if the written conditions are met and proven. Until they are proven, the default classification is `Unverified` and the default containment state is `unknown`.

Project state, without exaggeration:

- Local blocking: implemented in phase B, in .NET 8, over a local store. The behaviour is proven only in local tests.
- External containment: the adapters for Entra, grant removal, runtime isolation and the old-token probe are simulated, with fault injection. There is no real adapter for Entra or for Graph.
- Phases C, D and E: not executed. Tests BT-05, BT-06, BT-14, BT-17 and BT-20 are only proven in phases C and D. Locally there is, at most, a simulated approximation that does not count as proof.
- Nothing in this document was measured in a tenant. All the times cited come from the documentation.

Legend: Verified (text read on the page), Inference (conclusion drawn from verified texts), NOT VERIFIED (the documentation read does not state it). All sources are Microsoft Learn pages verified on 2026-11-11, with the prefix `https://learn.microsoft.com/en-us/` when abbreviated. The Lxx codes refer to `permission-matrix.md`.

## 2. Definitions

### Coverage classification

| Classification | Meaning | Condition to assign it |
| --- | --- | --- |
| `GatewayEnforced` | Every data action of the agent, within the declared scope, goes through the gateway and the executor. | Inventory of alternative paths done and empty, for the declared scope. The agent has no grant of its own and no credential of its own for the resource. Negative direct-access test executed. `direct_access_verified_absent` true, with a recent `last_verified_at`. |
| `DirectAccessPossible` | A direct path to the resource exists, or cannot be excluded. | A grant on the agent itself, a shared credential, or local access to the resource is enough. Grants on the agent itself count as a direct path. |
| `Unverified` | There is not yet enough inventory or testing. | It is the default value. |

The classification applies to a scope (resources and operations) and is never universal.

### Containment state

Global states: `requested`, `local_blocked`, `external_pending`, `verified_scoped`, `partial`, `failed`, `unknown`. Per-step states: Requested, Executed, Verified, Failed, Unknown.

- `verified_scoped` requires all steps to be verified by probe and the scope to be explicit. A "previous token refused" step is only Verified if the probe shows the refusal. If the probe shows that the token was accepted for a measured time, the step is Failed, the global state is `partial` and the measured time stays visible.
- Any Unknown step gives `unknown`. A mix of verified and unverified steps gives `partial`. All failed gives `failed`.
- `suspended` never becomes `verified_scoped` on its own. Administrative confirmation (the API responded) is not confirmation by probe.

### Local blocking is common to all types

In all types, the local blocking is the same and is the only part the product controls on its own. The suspension persists, in one transaction, the `Suspended` state, the new `revocation_epoch`, the containment operation in `LocalBlocked` and the outbox message. After that, the executor refuses new admissions. The specification sets as an objective to refuse new admissions within 5 seconds of the persisted confirmation of the suspension in the pilot environment. It is an objective, still without real measurement.

What local blocking does not do: it does not invalidate tokens already issued by Microsoft, it does not cancel a call already sent to Graph, and it does not remove from the agent grants that it holds directly on the resource. Actions already admitted and in progress are listed and reported separately (`InFlightActionIds`).

## 3. Summary table

| Type | Local blocking (gateway) | Documented external action | Tokens already issued | CAE | Maximum classification | Maximum containment state |
| --- | --- | --- | --- | --- | --- | --- |
| Entra Agent ID | Yes, common | Disable agent identity (v1.0). Disable agent user. Remove grants on the resource. Revoke agent user sessions (inference) | Outcome NOT VERIFIED | NOT VERIFIED | `GatewayEnforced` if the inventory proves absence of direct grants. Otherwise `DirectAccessPossible` or `Unverified` | `verified_scoped` only for the tested resource and only if the previous-token probe is refused. Otherwise `partial` |
| Service principal | Yes, common | `accountEnabled` equal to false (v1.0). Remove grants on the resource. Rotate credentials | No direct statement. Valid until they expire, by analogy (inference) | Yes, with narrow restrictions | `GatewayEnforced` if the inventory proves absence of direct grants | `verified_scoped` only for Graph and for the tested resource, with CAE `cp1` active or a favourable probe. Otherwise `partial` |
| Managed identity | Yes, common | No documented disable. Remove grants on the resource. Stop or isolate the runtime | Cache of about 24 hours, no forced refresh | No | `GatewayEnforced` if the identity is not shared and the inventory proves absence of direct grants | `verified_scoped` only for the resource where the probe confirms refusal of the previous token. For the rest `partial` |
| Copilot Studio | Only if the calls go through the executor | Block in the Microsoft 365 admin centre (excerpt). If it is a classic application, the service principal rows apply | NOT VERIFIED | NOT VERIFIED | `Unverified` | `unknown`, or `partial` on a verified step |
| Foundry | Only if the calls go through the executor | Like Copilot Studio. It may be a classic application or service principal | NOT VERIFIED | NOT VERIFIED | `Unverified` | `unknown`, or `partial` on a verified step |
| MCP | Only on the tools served by the executor | Depends on the MCP server. No Entra action if the server uses an API key | Depends on the server | Not applicable | `Unverified` today. Theoretical ceiling `GatewayEnforced` limited to the executor tools, outside the MVP | `unknown`, or `partial` |
| Third parties | Only if the calls go through the executor | Remove consent and grants in the tenant. Workload identities policies do not cover multitenant apps or third-party SaaS | Outside the control of the tenant | Out of scope (documented) | `Unverified` | `partial` at best, `unknown` by default |

## 4. Types inside the MVP

### 4.1 Entra Agent ID

Verified. Entra Agent ID has four objects: blueprint, blueprint principal, agent identity and agent user. The agent identity is a service principal with `servicePrincipalType` equal to `ServiceIdentity`. The credentials stay in the blueprint and are shared by all child identities: "Treat each blueprint as a credential boundary". Source: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity`.

Verified. There are three execution modes, each with a different token subject: S2S (the agent identity is the subject), OBO (the user is the subject and the agent identity is the actor) and Agentic-User (the agent user). Same source.

**What is blocked locally.** New admissions in the executor, by state and epoch. The executor has its own identity per agent (`ExecutorObjectId`) and the agent does not receive the data credential. If the agent has no grants of its own on the resource, local blocking closes the only path the product controls.

**External action and documentary evidence.**

- Disable the agent identity: `PATCH /servicePrincipals/{id}/microsoft.graph.agentIdentity`, v1.0, permission `AgentIdentity.EnableDisable.All`, Agent ID Administrator role. Declared effect: "block its access and token issuance". Sources: `https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0` and `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin`. The exact request body is inference (L04).
- Disable the agent user: `accountEnabled` property, v1.0. Source: `https://learn.microsoft.com/en-us/graph/api/agentuser-update?view=graph-rest-1.0`. Disabling the agent identity is not documented as disabling the agent user (L06).
- Remove Selected grants on the resource (G03, G04). Verified: "Application access is lost as soon as a scope is revoked." No numeric value.
- Revoke agent user sessions: `revokeSignInSessions`, v1.0. Its application to agent users is inference (L13).
- Remove Azure RBAC assignments of the agent identity, if any exist. Verified: blueprints cannot have RBAC roles, they are assigned to the agent identity.
- Disabling the blueprint principal disables all its agent identities: "will no longer be able to authenticate". It is a wider scope, with a separate approval. Individual disabling is the default action (specification, section 8). Source: `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-blueprint`.
- Tenant-wide Conditional Access policy: blocks issuance for whole categories without changing objects, in Graph beta and with a licence requirement that is contradictory between pages. It is not a per-agent action. Source: `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities`.
- Block in the Microsoft 365 admin centre (search excerpts): second switch. Whether it is the same state as `accountEnabled`: NOT VERIFIED (L16).

**Tokens already issued.** NOT VERIFIED. No Agent ID page read says what happens to access tokens already issued to an agent identity. All the language is about future issuance. Prudence: assume validity until expiry. See section 6.

**CAE.** NOT VERIFIED for agents. The only mention is "Some resources also support Continuous Access Evaluation". It is not known which resources, which events, or whether disabling an agent identity is a revocation event. Source: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id`. Risk inference: a client that declares `cp1` starts receiving tokens of up to 24 hours, and outside Graph revocation by CAE does not exist.

**Limits of Conditional Access for agents (verified).** It does not cover the blueprint when it acquires a token to create identities. It does not cover the intermediate token exchange at the `AAD Token Exchange Endpoint: Public`. The policy on the agent identity does not cover OBO requests or the agent user. It does not apply with Security defaults. It does not apply to access by API key.

**Alternative paths to inventory.** See section 7. In particular: the OBO flow and the agent user are distinct token subjects, and the blueprint credentials are shared by sibling identities.

**Maximum honest classification.** `GatewayEnforced`, for the declared scope (site, library, operations), if: the agent identity has no grant of its own on the resource; the executor has its own identity; the agent user, if one exists, also has no access; and the negative direct-access test passed. If a broad or own grant exists, it drops to `DirectAccessPossible`. Without inventory, `Unverified`.

**Maximum containment state.** `verified_scoped`, with this explicit scope: new admissions refused at the gateway, new token issuance by the agent identity refused by probe, grant on resource R removed and read refused by probe, previous token refused on R by probe (BT-14). The scope does not include other resources that the agent identity can reach. If the previous-token probe is not executed or shows acceptance, the state is `partial`, with the measured time. If the disabling cannot be verified, `unknown`.

### 4.2 Service principal

Verified. Disabling: `PATCH https://graph.microsoft.com/v1.0/servicePrincipals/{id}` with `{"accountEnabled": false}`, permission `Application.ReadWrite.All`, roles Cloud Application Administrator, Application Administrator, or owner. Declared effect: it prevents the issuance of tokens "for that application", in a single tenant. Source: `https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/disable-user-sign-in-portal`.

**What is blocked locally.** The same common blocking, in the executor. If the service principal is the identity that the agent uses directly for Graph, local blocking does not stop it from talking to Graph outside the executor. In that case the classification drops.

**External action and documentary evidence.**

- `accountEnabled` equal to false (v1.0, verified).
- Disable the application (`isDisabled`): global scope, in all tenants. The page uses beta to disable and v1.0 to re-enable. Verified: "it immediately stops receiving new access tokens, but existing tokens remain valid until they expire". Source: `https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/deactivate-app-registration`.
- Remove Selected grants and consents.
- Rotate credentials, when applicable.
- Conditional Access for workload identities: only "Block access", only single-tenant, requires Workload Identities Premium, evaluated only when the service principal requests a token. Source: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity`.

**Tokens already issued.** The `accountEnabled` page does not say. It only states that it prevents issuance. The only source that links "service principal disable" to existing tokens is the one on CAE for workload identities. Inference: outside the CAE conditions, tokens remain valid until they expire. See section 6.

**CAE.** Applicable, with narrow restrictions (verified). Only requests to Microsoft Graph as the resource. Only single-tenant service principals of LOB applications. Only if the client declares `cp1`. Revocation events: "Service principal disable", "Service principal delete" and "High service principal risk". Multitenant applications and third-party SaaS are out of scope. Managed identities are not supported. No numeric deadline for service principals: the introduction says "instantly", the users page admits up to 15 minutes. Source: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation-workload`.

**Maximum honest classification.** `GatewayEnforced` if the service principal that the agent uses has no grant of its own on the resource and the executor uses another identity. `DirectAccessPossible` if the agent's own service principal has broad or Selected grants.

**Maximum containment state.** `verified_scoped` for Graph and for the tested resource, if the previous-token probe is refused (by CAE with `cp1`, or by removing the grant on the resource). Otherwise `partial`, with the measured time. For resources other than Graph, CAE does not help and it is `partial` or `unknown`.

### 4.3 Managed identity

Verified. In the pages read there is no Entra disabling procedure equivalent to `accountEnabled` for managed identities. Whether `accountEnabled` is supported and effective on the service principal of a managed identity: NOT VERIFIED (L11).

**What is blocked locally.** The same common blocking. It is the main control for this type, because the Entra controls are the weakest of the three types.

**External action and documentary evidence.**

- Remove the grant on the resource: "Remove the identity's access to the target resource". For Logic Apps (excerpt): disable the system-assigned identity or remove the user-assigned one. Source: `https://learn.microsoft.com/en-us/azure/logic-apps/authenticate-with-managed-identity`.
- Stop or isolate the runtime to which the identity is attached. Verified: "The security boundary of the identity is the resource to which it's attached." Source: `https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq`.
- Deleting the identity is not an emergency action (specification, section 8). It stays in soft delete for 30 days and the previous tokens remain valid.
- Conditional Access: "Managed identities aren't covered by policy." The page suggests access reviews as an alternative. Source: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity`.

**Tokens already issued (verified).** Token cache of about 24 hours per resource URI. "Today, it isn't possible to force a managed identity's token to be refreshed before its expiry." After the identity is deleted, the previous tokens remain valid until the original expiry. Some systems, such as Azure RBAC, "will continue to accept requests from that token until it expires". Permission changes may take several hours. Configurable lifetime is not supported. Source: the same FAQ and `https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes`.

**CAE.** Not applicable. "Managed identities aren't currently supported by CAE for workload identities."

**Design warning.** The documentation recommends federating the blueprint to a managed identity so as not to store a secret. The effect of the managed identity's 24-hour cache on the revocation of a federated blueprint: NOT VERIFIED. The specification forbids reusing the same user-assigned managed identity across runtimes with incompatible scopes. A shared identity degrades the classification.

**Maximum honest classification.** `GatewayEnforced` if the identity is not shared and the inventory proves it has no direct grants on the resource. Any direct grant, or sharing with another runtime, gives `DirectAccessPossible`.

**Maximum containment state.** `partial` by default. It can reach `verified_scoped` only for the SharePoint resource where the probe confirms that the previous token is refused after the grant is removed (inference, to be measured in BT-14). For any other resource that the identity can reach, `partial`, because Entra offers no revocation and validity can reach 24 hours.

## 5. Cases outside the MVP

The specification (section 2) excludes from the MVP the promise of control of Copilot Studio, Foundry, MCP and third parties. The lines below are not promises. They serve to say what the honest ceiling is and what remains to be inventoried.

### 5.1 Copilot Studio

- Verified: agents created in earlier versions of Copilot Studio may exist as classic applications or service principals and "don't have Microsoft Entra agent identities". Source: `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities`. In that case the service principal row applies.
- Search excerpts (pages not read in full): blocking an agent created in SharePoint or Foundry "only impacts its availability in Microsoft Copilot Chat". Blocking an instance "stops it and any actions it's performing". Sources: `https://learn.microsoft.com/en-us/microsoft-agent-365/admin/agent-actions` and `https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-agent-instances`. Block in the admin centre is not, by itself, revocation of access to data.
- Local: blocking in the executor only holds if the agent calls the executor. A hosted agent that uses its native connectors does not go through the gateway.
- CAE and tokens: NOT VERIFIED.
- Ceiling: `Unverified`. State: `unknown`, or `partial` if a step is executed and verified.

### 5.2 Foundry

- The same text as Copilot Studio applies on classic applications and Block.
- Verified: the APIM AI gateway governs model traffic (token limit, semantic cache, content safety, logging). "AI gateway in Microsoft Foundry" is in preview. Source: `https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities`. Inference: it does not offer fine-grained authorisation per tool or a wallet per agent.
- Azure resources that the Foundry runtime accesses may have their own Azure RBAC, outside the control of the wallet.
- Ceiling: `Unverified`. State: `unknown`, or `partial`.

### 5.3 MCP

- Verified: APIM supports remote MCP servers and A2A APIs, and allows exposing REST APIs as MCP servers. Source: `https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities`.
- Local: the blocking holds for tools that go through the gateway and the executor with the executor's credential. A third-party remote MCP server, or one local to the runtime, stays outside.
- External action: depends on the server. If it uses an API key, Entra is not involved and Conditional Access does not apply (verified).
- Ceiling today: `Unverified`. Theoretical ceiling, if it is ever designed: `GatewayEnforced` limited to the tools served by the executor. State: `unknown`, or `partial`.

### 5.4 Third parties

- Verified: Conditional Access for workload identities does not cover "Microsoft and third-party SaaS applications, including multitenant apps". CAE for workload identities: "Third-party SaaS and multitenant apps are out of scope". Sources: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity` and the CAE page for workload identities.
- Verified: access by API key bypasses the Entra pipeline entirely.
- Possible external action in the tenant: disable the local service principal (single-tenant scope), remove consents and grants. It does not reach tokens or keys issued outside the tenant.
- Ceiling: `Unverified`. State: `partial` at best, `unknown` by default.

## 6. Tokens already issued: documented times and gaps

All values come from the documentation and were not measured. The contradictions between pages are flagged. Use the worst case in risk analyses.

| Item | Documented time | Source | Gap |
| --- | --- | --- | --- |
| Default access token (registered APIs) | 60 to 90 minutes, average 75. Other pages say "1 hour". Without Conditional Access, Teams and Microsoft 365 have 2 hours | `https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes`, `https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens`, `https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access` | The `access-tokens` page says the range does not apply to Microsoft APIs, such as Graph. The value for Graph tokens is not guaranteed. Use 90 minutes as the worst case without CAE |
| Agent identity access token | NOT VERIFIED | none | No agent-specific page |
| Configurable lifetime | Minimum of 10 minutes, maximum of 23:59:59. Not supported for managed identities. Not respected in CAE sessions | `https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes` | Applicability to agent identities not verified |
| User CAE token | Up to 28 hours (other pages: 24 to 28, 20 to 28) | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation` | Use 28 hours as the worst case |
| Critical CAE events for users | "latency of up to 15 minutes might be observed" | same page | Do not promise instant revocation |
| Workload identity CAE token | Up to 24 hours | CAE page for workload identities | No numeric deadline for the enforcement of the revocation event |
| Revocation of user sessions | Delay of "a few minutes" for refresh tokens. It does not invalidate access tokens | `https://learn.microsoft.com/en-us/graph/api/user-revokesigninsessions?view=graph-rest-1.0` | Application to the agent user is inference |
| Disable application (`isDisabled`) | Existing tokens valid until they expire | `https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/deactivate-app-registration` | The same sentence is not written for `accountEnabled` of the service principal |
| Managed identity | Cache of about 24 hours. No forced refresh. Tokens valid after the identity is deleted | Managed identities FAQ | Effect on federated blueprint not verified |
| Managed identity permission changes | "several hours" | Managed identities FAQ | No maximum value |
| SharePoint pre-authenticated download URL | "within minutes" on one page, "1 hour" on another. Removing permissions "might not immediately invalidate the URL" | `https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0`, `https://learn.microsoft.com/en-us/graph/api/resources/driveitem?view=graph-rest-1.0` | Use 1 hour as the worst case. Each URL issued is a bearer credential |
| Revocation of a Selected grant | "Application access is lost as soon as a scope is revoked." No numeric value | `https://learn.microsoft.com/en-us/graph/permissions-selected-overview` | Propagation time NOT VERIFIED. The effect on an already issued token that carries the scope is inference (the resource stores the tuple of application, resource and role) |
| RBAC for Applications cache (Exchange, later phase) | 30 minutes to 2 hours, depending on usage | `https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac` | The test cmdlet ignores the cache |
| Application deprovisioning synchronisation | Normally 20 to 40 minutes | `https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access` | Relevant only for applications with provisioning |

Verified conclusion for the containment layer: disabling an identity and blocking issuance does not invalidate access tokens already issued. The only documented way to revoke access tokens is CAE, and only under the restricted conditions above. For that reason the proof of containment rests on probes with a previous token (BT-14), executed on a test resource, with the real result reported.

## 7. Alternative paths to inventory

Each path found lowers the classification until it is eliminated or contained. This table is the inventory list. The state of each item in the tenant is still to be filled in (see `environment-assessment.md`).

| Path | Applies to | Why it counts |
| --- | --- | --- |
| Selected or broad grants on the agent itself (`Sites.Read.All`, `Files.Read.All`, `Sites.ReadWrite.All`) | All | A grant of its own is a direct path. Revoking a higher-level scope does not remove access given by a lower scope |
| Selected grants at lower levels (list, item, file) | All | Access persists after removing only the site grant |
| Blueprint credentials shared by sibling identities | Entra Agent ID | Whoever holds the credential obtains tokens for any agent identity of the blueprint |
| Agent user and OBO flow | Entra Agent ID | Distinct token subjects. Policy and disabling of the agent identity do not cover them in a documented way |
| User-assigned managed identity reused across runtimes | Managed identity, federated blueprint | A runtime with a different scope uses the same token subject |
| Executor Graph credential accessible to the agent runtime | All | It destroys the separation. The agent never receives the executor's credential |
| Pre-authenticated URLs already issued | All | They download without a token for up to 1 hour. The executor does not return them to the agent |
| `$batch` and `/beta/$batch` | All | The external request is always `POST`. The real methods and paths go in the body (inference) |
| `$expand` and `$select` on metadata | All | `$expand=children` lists children. `@microsoft.graph.downloadUrl` hands over the capability to download |
| Graph search (`/search/query`) | All | Behaviour with Selected NOT VERIFIED. There is only a forum answer saying it does not respect it |
| Local accesses to the resource (site members, sharing links) | All | The effective access of the user in OBO and the existing links do not go through the gateway |
| API keys and secrets stored in the runtime | All | They bypass Entra. Conditional Access does not apply |
| Azure RBAC assigned to the identity | Agent ID, service principal, managed identity | Azure RBAC may keep accepting the token until it expires |
| Connectors, MCP tools and Power Platform of the agent | Outside the MVP | They may have their own connection to the resource |
| APIM identity and policy editing | Gateway | Whoever edits policies can use the APIM managed identity and export the token. The data credential stays in the executor, not in APIM |
| Open user sessions and refresh tokens | Agent user, OBO | `revokeSignInSessions` covers refresh tokens, not access tokens |
| Runtime egress to Graph | All | Restricted egress is an additional defence and does not replace removing permissions (specification, section 5) |

Structural limit, verified and inferred: there is no documentation that allows restricting Microsoft Graph to accept requests only from an APIM. Whoever has a valid Graph token calls Graph directly. What prevents the bypass is that the agent does not possess the credential. For that reason the `GatewayEnforced` classification depends on proving that the agent has no credential and no grant of its own, and not on network rules.

## 8. What is never promised

Based on sections 2 and 10 of the specification, and on the honesty rules of section 0.

- Absolute absence of failures. The promise is replaced by verifiable requirements, defence in depth, failure rehearsals and explicit limits.
- "Universal access revoked". `verified_scoped` requires explicit scope and declares the resource and the operations.
- Instant revocation of all tokens. Tokens already issued have documented or undocumented windows, and the product measures them, it does not presume them.
- Conditional Access as instant revocation. It is evaluated at issuance and at renewal.
- CAE as a universal guarantee. It depends on the resource, the client and the identity.
- Control of all Copilot Studio, Foundry, MCP and third-party agents. They are outside the MVP.
- Deleting data already exfiltrated.
- Universally cancelling operations already accepted by external services. Revalidation does not eliminate the race with a call already sent to Graph.
- Universal rollback and universal exactly-once for Graph effects.
- Converting `suspended` to `verified_scoped` automatically. A 200 response, a green dashboard or a demonstration with simulation do not prove containment.
- A universal deadline for external containment. It is measured per identity and per resource.
- Coverage of any agent by Agent 365, Defender, Purview or Sentinel, or synchronous blocking of any action.
- That the ledger prevents a false initial entry, guarantees availability or recovers tampered data. It gives evidence of tampering through verification.
- Automatic GDPR compliance from using Microsoft services.
- That APIM turns model governance into fine-grained tool authorisation.

## 9. Gaps that limit the ceilings

| Gap | Effect | How to close |
| --- | --- | --- |
| Outcome of tokens already issued to agent identities and to service principals outside CAE | The `verified_scoped` ceiling of Agent ID and service principal depends on the probe | Phase D, BT-14, test resource |
| CAE for agent identities | Without it, the window can go up to the expiry of the token | Read future documentation. Test in tenant |
| Managed identity disable | Containment rests on grants and runtime | L11 in `permission-matrix.md` |
| Block in the Microsoft 365 admin centre versus `accountEnabled` | Two switches with no documented relationship | L16 |
| Cascade effect on agent user and OBO | May leave token subjects active | L06 |
| Inventory of alternative paths in the tenant | Without it the classification is `Unverified` | Inventory in phase C |
| Propagation time of Selected grant revocation | The "grant removed" step has no deadline | Measure in phase C |
| Effect of the managed identity cache on a federated blueprint | May widen the revocation window | Test in phase D |
