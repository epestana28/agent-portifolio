# Environment assessment

Status: draft, to be validated on the tenant

Verified against official documentation on 2026-11-11

## 1. Scope and real status

This document fulfils section 4 of the specification. There is no tenant, so it is an assessment in two parts per topic.

The part "What the official documentation establishes" holds only what was read on Microsoft Learn, with a URL. The part "To be filled in the tenant" lists the concrete fields that only the tenant can answer and the general way to obtain them. No tenant value was invented.

Project status, without exaggeration:

- Phase A (validation documents): written from official documentation, without a tenant.
- Phase B (local vertical slice on .NET 8): implemented without real credentials. The Microsoft world is simulated and is marked as a simulation.
- Phases C, D and E: not executed. The Bicep is written and has never been applied. There is no real adapter for Entra or for Graph.

Legend used throughout the document:

- Verified: text read on the cited page.
- Inference: a conclusion drawn from two or more texts. The documentation does not state it.
- To be validated on the tenant: only a real test answers it.
- NOT VERIFIED: the documentation read does not state it. It does not mean it is false.

All sources were read on Microsoft Learn and verified on 2026-11-11. URL prefix: `https://learn.microsoft.com/en-us/`. When a page was only read through a search excerpt, this is stated and the claim must be reconfirmed before it is used as fact.

## 2. Tenant, subscription, region and data classification

### What the official documentation establishes

- Agent identities are single-tenant and only receive tokens in the tenant where they were created. Blueprints can be multitenant. Source: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity`.
- Agent 365 is GA for the Commercial segment, per user, since 2026-05-01. Source: `https://learn.microsoft.com/en-us/microsoft-agent-365/overview`. Availability in GCC, GCC High and DoD: NOT VERIFIED.
- CAE is available in Azure Government tenants (GCC High and DoD) for Exchange Online. Source: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation`.
- Azure Confidential Ledger replicates the data to the regional pair and does not allow that replication to be changed or disabled. Source: `https://learn.microsoft.com/en-us/azure/confidential-ledger/secure-confidential-ledger`.
- Automatic management of SQL Ledger digests with immutable Blob does not accept LRS accounts. Source: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits`.
- The documentation read does not define data classification and does not mention GDPR. The specification (section 11) forbids declaring automatic GDPR compliance for using Microsoft services.
- Inference: ledger tables do not allow rows to be deleted and a container with a locked policy does not allow blobs to be deleted before the end of the retention. For that reason, personal data must not enter them.

### To be filled in the tenant

| Field | General way to obtain | Value |
| --- | --- | --- |
| Tenant ID | Tenant properties in the Entra admin centre | TO BE FILLED IN THE TENANT |
| Tenant type (pilot, production, shared) | Confirmation from the tenant owner | TO BE FILLED IN THE TENANT |
| Cloud segment (Commercial or sovereign) | Confirmation from the tenant owner | TO BE FILLED IN THE TENANT |
| Azure subscription of the pilot | List of subscriptions in the Azure portal | TO BE FILLED IN THE TENANT |
| Primary region and paired region | Architecture decision, validated against the regions that offer each service | TO BE FILLED IN THE TENANT |
| Participation in the Frontier preview programme | Confirmation in the Microsoft 365 admin centre or with the tenant owner | TO BE FILLED IN THE TENANT |
| Data classification of the pilot site and library | Internal classification policy, applied to the chosen site | TO BE FILLED IN THE TENANT |
| Existence of personal data in the pilot documents | Review of the content with the site owner | TO BE FILLED IN THE TENANT |

## 3. Licences and incremental costs

### What the official documentation establishes

| Capability | Licence stated | Source |
| --- | --- | --- |
| Entra Agent ID platform (create and manage identities and blueprints) | "Agent ID is available for all Microsoft Entra customers." | `https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id` |
| Entra security extended to agents | Requires Microsoft Agent 365. Included in Microsoft 365 E7. Add-on for E5, A5 and Business Premium | same page |
| Conditional Access for agents (page dated 2026-07-31) | Microsoft 365 E7, or an Agent 365 licence with at least Entra P1 or Microsoft 365 E3 | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id` |
| Conditional Access for agents (administration page) | "Applying Conditional Access policies in your tenant requires the Microsoft Entra ID P1 license." | `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities` |
| ID Protection for agents | Entra ID P2 during the preview. An excerpt from another page says it will start to require an Agent 365 licence, with no date | `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin` and `https://learn.microsoft.com/en-us/entra/fundamentals/licensing` (excerpt) |
| ID Governance for agents | E7, or Agent 365 with at least Entra P1 or Microsoft 365 E3 (excerpt) | `https://learn.microsoft.com/en-us/entra/id-governance/agent-id-governance-overview` |
| Conditional Access for service principals | Workload Identities Premium to create or change policies. Existing policies keep working without a licence, but cannot be changed | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity` |
| Agent 365 | Per user. At least one user must have a qualifying licence assigned. Works best with Microsoft E5 as a prerequisite | `https://learn.microsoft.com/en-us/microsoft-agent-365/overview` |
| Agent 365 telemetry | With no licence assigned, the request returns `200 OK` and each span is `rejected` with `tenant_not_licensed`. Having the SKU present in the tenant is not enough | `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability-concepts` |

Licence contradiction (flagged in the research as C1). Two official pages give different requirements for Conditional Access applied to agents: Entra ID P1 (administration page) against Microsoft 365 E7 or Agent 365 with P1 or E3 (Conditional Access for agents page, the most recent). Treatment in this project: document Agent 365 as the probable requirement, cite the divergence and decide on the tenant.

Second divergence (C2). ID Protection for agents requires P2 "during preview", but another page says that "Starting soon" it will require an Agent 365 licence. The requirement is in transition, with no date.

Agent 365 prices: NOT VERIFIED. The pages point to `microsoft.com/microsoft-agent-365`, outside Microsoft Learn. No values are written.

Licence requirement per Graph operation (SharePoint Selected, disabling identities, revoking sessions): no page read states it. The absence of a mention does not prove that no requirement exists. See `permission-matrix.md`.

### To be filled in the tenant

| Field | General way to obtain | Value |
| --- | --- | --- |
| SKUs present in the tenant | Licences page of the Microsoft 365 admin centre | TO BE FILLED IN THE TENANT |
| SKUs with a licence actually assigned (Agent 365, E7, E5, E3) | Per-user assignments. Confirm that at least one user has the licence assigned | TO BE FILLED IN THE TENANT |
| Entra ID P1 and P2 | Licences page of Entra | TO BE FILLED IN THE TENANT |
| Workload Identities Premium | Licences page of Entra | TO BE FILLED IN THE TENANT |
| Defender, Purview and Sentinel (optional add-ons) | Existing licences and subscriptions | TO BE FILLED IN THE TENANT |
| Incremental cost per licence | Tenant contract and official pricing pages. See `cost-estimate.md` | TO BE FILLED IN THE TENANT |
| Who approves the incremental cost | Owner of the pilot budget | TO BE FILLED IN THE TENANT |

## 4. Available features, GA or preview, region and cloud restrictions

### What the official documentation establishes

The "Status" column transcribes what the page says. When the page does not use the words GA or preview, the row says "not stated". GA is not assumed because the API is in v1.0.

| Feature | Documented status | Source |
| --- | --- | --- |
| Entra Agent ID, product | "Available for all Microsoft Entra customers". The word GA does not appear. NOT VERIFIED as a textual claim | `https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id` |
| Graph APIs for agentIdentity, blueprint, principal and agentUser | Published in v1.0. Inference: indicates production support, without a GA statement | `https://learn.microsoft.com/en-us/graph/api/resources/agentidentity?view=graph-rest-1.0` |
| Conditional Access policies for agents through Graph | Examples in `beta` | `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities` |
| Agent sign-in logs through Graph | Example in `beta` | `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin` |
| "Agent execution environments" condition | Preview | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id` |
| ID Protection for agents | Preview | `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin` |
| Blueprint manifest editor | Preview | `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-blueprint` |
| Agent ID lifecycle policies | Preview (search excerpt, page not read in full) | `https://learn.microsoft.com/en-us/entra/id-governance/agent-lifecycle-policies` |
| Agent 365 | GA since 2026-05-01, Commercial segment | `https://learn.microsoft.com/en-us/microsoft-agent-365/overview` |
| Agent user account and Agentic-User mode in Agent 365 | Only in tenants in the Frontier preview programme | `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity` |
| Agent 365 Observability SDK | Deprecated. The Microsoft OpenTelemetry Distro is recommended | `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability-concepts` |
| CAE for users | Documented without a preview mark | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation` |
| CAE for workload identities | Documented without a preview mark, with restrictions (see section 6) | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation-workload` |
| CAE for agent identities | NOT VERIFIED. The only mention is "Some resources also support Continuous Access Evaluation" | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id` |
| Selected scopes (Sites, Lists, ListItems, Files) | GA or preview not stated per level. Delegated and application modes supported | `https://learn.microsoft.com/en-us/graph/permissions-selected-overview` |
| Permission assignment on site, list and item | Present in v1.0 without a beta warning. Removal on list and item only found in beta. Inference: general availability of the API, without a GA statement for the scope | Microsoft Graph pages `site-post-permissions`, `list-post-permissions`, `listitem-post-permissions` |
| Exchange RBAC for Applications | GA or preview not stated. Opposing signals: a preview contact on the page and the consolidated Entra view "upcoming" | `https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac` |
| Application Access Policies (Exchange) | Legacy. "New access configuration should not use Application Access Policies". No deprecation date | `https://learn.microsoft.com/en-us/exchange/permissions-exo/application-access-policies` |
| APIM AI gateway | Part of APIM. "Unified model API" and "AI gateway in Microsoft Foundry" in preview. Availability varies by tier | `https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities` |
| APIM `validate-azure-ad-token` | "Microsoft Entra ID for customers (preview) is not supported" | `https://learn.microsoft.com/en-us/azure/api-management/validate-azure-ad-token-policy` |
| SQL Ledger in Azure SQL Database | No preview mark. Literal GA phrase: NOT VERIFIED | `https://learn.microsoft.com/en-us/azure/azure-sql/database/ledger-landing` |
| Azure Confidential Ledger | GA (search excerpt) | `https://learn.microsoft.com/en-us/azure/confidential-computing/overview-azure-products` |
| Immutable Blob | Stable documentation. Incompatible with point-in-time restore and last access tracking. No support with NFS 3.0 or SFTP | `https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview` |
| Bicep apiVersions for 2026 | Documented. Actual acceptance by the Bicep CLI and by each region: NOT VERIFIED | Azure template reference on Microsoft Learn |

Rule of the specification (section 0, point 8): a preview feature is not a mandatory production dependency without explicit acceptance of the risk and a documented alternative. It applies to ID Protection for agents, the "Agent execution environments" condition, lifecycle policies and agent user.

### To be filled in the tenant

| Field | General way to obtain | Value |
| --- | --- | --- |
| Each feature in the table exists and is visible in the tenant | Check in the Entra and Microsoft 365 admin centres | TO BE FILLED IN THE TENANT |
| Each Azure service is available in the chosen region | Availability by region in the documentation of each service, confirmed in the portal when the resource is created | TO BE FILLED IN THE TENANT |
| GA or preview status of the Selected scopes per level | Assignment and read test on the tenant. The documentation does not state it | TO BE FILLED IN THE TENANT |
| Status of RBAC for Applications in the tenant | Confirmation with the Exchange administrator and an assignment test | TO BE FILLED IN THE TENANT |
| Decision on each preview dependency (accept, postpone, alternative) | Risk approval, see section 10 | TO BE FILLED IN THE TENANT |

## 5. Existing runtimes and identity types

### What the official documentation establishes

- Object model of Agent ID: blueprint (inherits from `application`), blueprint principal (inherits from `servicePrincipal`), agent identity (service principal with `servicePrincipalType` equal to `ServiceIdentity`) and agent user (a subtype of user, 1:1 with the agent identity). Source: `https://learn.microsoft.com/en-us/entra/agent-id/key-concepts`.
- A blueprint can have several agent identities. The credentials live only on the blueprint and are shared by all child identities: "Treat each blueprint as a credential boundary". Source: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity`.
- Blueprint credential types: federated identity credentials, certificates and keys, client secrets. For agents in Azure, the blueprint can be federated with a managed identity so that no secret is stored. Same source.
- Three execution modes: S2S (the agent identity is the subject of the token), OBO (the user is the subject and the agent identity is the actor) and Agentic-User. Same source.
- Agents created in earlier versions of Copilot Studio and Azure AI Foundry can be classic applications or service principals and "don't have Microsoft Entra agent identities". Source: `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities`.
- Classic Entra applications, such as those of custom engine agents, "aren't registered agent instances" in Agent 365. Creating the Entra identity does not register the instance. Source: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability-concepts`.
- A managed identity only has a service principal, with no application. The security boundary is the resource it is associated with. Source: `https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq`.
- There is a second control plane, the Microsoft 365 admin centre, with a Block action for agent and for instance (search excerpts). Whether Block changes `accountEnabled` in Entra: NOT VERIFIED.
- Design inference: the planned pilot runtime is Azure Container Apps, with a separate execution identity per agent. The real runtime of the pilot agent has not yet been chosen in this project.

### To be filled in the tenant

| Field | General way to obtain | Value |
| --- | --- | --- |
| Runtime of the pilot agent and who controls it | Team decision. Confirm that the runtime is controlled by the team (specification, section 2) | TO BE FILLED IN THE TENANT |
| Inventory of agent identities (objectId, appId, blueprint, state, sponsor) | List of agent identities in the Entra admin centre | TO BE FILLED IN THE TENANT |
| Inventory of blueprints and blueprint principals, and whether they are multitenant | List of blueprints in the Entra admin centre | TO BE FILLED IN THE TENANT |
| Existing agent users and the parent identity of each | Entra admin centre | TO BE FILLED IN THE TENANT |
| Service principals of applications with access to SharePoint or Graph | List of enterprise applications and consented permissions | TO BE FILLED IN THE TENANT |
| Managed identities (system and user-assigned), the resources they are associated with and whether any is shared between runtimes | List of managed identities in the Azure portal and assignments on the resources | TO BE FILLED IN THE TENANT |
| Copilot Studio, Foundry, MCP and third-party agents without an agent identity | Inventory in the Microsoft 365 admin centre and in the originating services | TO BE FILLED IN THE TENANT |
| Instances registered in Agent 365 | Agent inventory in the Microsoft 365 admin centre | TO BE FILLED IN THE TENANT |

## 6. Conditional Access by identity type

Warning. Conditional Access for users, agents, agent users, service principals and managed identities does not have the same coverage. The specification (section 4) requires each combination to be verified. Conditional Access is also not instant revocation, and blocking authentication does not prevent the creation of identities.

### What the official documentation establishes

| Identity type | Documented coverage | Source |
| --- | --- | --- |
| Users | Normal Conditional Access. Requires Entra ID P1 | `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities` |
| Agent identities | Policy with target "Agents". In Graph, `includeAgentIdServicePrincipals`. It does not cover the blueprint when it acquires a token to create identities, nor the intermediate token exchange at `AAD Token Exchange Endpoint: Public` | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id` |
| Agent identities, OBO flow | "Selecting an agent identity as the policy subject doesn't cover agent requests made on behalf of the user." | same page |
| Agent users | Separate target. A policy on the agent identity does not apply to the agent user and vice versa. Policies with the "Users" assignment do not apply to agent users | same page |
| Service principals (workload identities) | Only single-tenant ones registered in the tenant. Only "Block access". Requires Workload Identities Premium. Assignment to a group is not enforced on the service principal. Evaluated only when the service principal requests a token | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity` |
| Managed identities | "Managed identities aren't covered by policy." The page suggests access reviews as an alternative | same page |
| Multitenant applications and third-party SaaS | Not covered by workload identity policies | same page |
| Access by API key | Bypasses the Entra pipeline entirely. Conditional Access does not apply | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id` |
| Security defaults enabled | Agent policies do not apply | `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities` |

General limits verified:

- Conditional Access is evaluated when Entra issues or renews a token. Source: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id`.
- The three tenant-wide agent blocking policies prevent authentication but not the creation of agent identities. Source: `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities`.
- CAE for users: critical events with latency of up to 15 minutes, CAE tokens of up to 28 hours, no support for guests. Source: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation`.
- CAE for workload identities: only Microsoft Graph as the resource, only single-tenant LOB service principals, only with the `cp1` capability declared by the client, tokens of up to 24 hours, managed identities not supported. Source: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation-workload`.
- Whether a workload identity policy with `ServicePrincipalsInMyTenant` also catches agent identities: NOT VERIFIED.

### To be filled in the tenant

Fill in one row per combination that the pilot uses. Each cell is to be tested.

| Identity | Resource called | Client | Policy that applies | Policy state (report-only or enabled) | Tested (yes or no) | Observed result |
| --- | --- | --- | --- | --- | --- | --- |
| Pilot agent identity (S2S) | Graph (SharePoint) | executor | TO BE FILLED IN THE TENANT | TO BE FILLED IN THE TENANT | no | TO BE FILLED IN THE TENANT |
| Pilot agent identity (OBO) | Graph (SharePoint) | TO BE FILLED IN THE TENANT | TO BE FILLED IN THE TENANT | TO BE FILLED IN THE TENANT | no | TO BE FILLED IN THE TENANT |
| Agent user, if one exists | Graph (SharePoint) | TO BE FILLED IN THE TENANT | TO BE FILLED IN THE TENANT | TO BE FILLED IN THE TENANT | no | TO BE FILLED IN THE TENANT |
| Pilot service principal | Graph (SharePoint) | TO BE FILLED IN THE TENANT | TO BE FILLED IN THE TENANT | TO BE FILLED IN THE TENANT | no | TO BE FILLED IN THE TENANT |
| Pilot managed identity | Graph (SharePoint) | TO BE FILLED IN THE TENANT | Not covered by the documentation | Not applicable | no | TO BE FILLED IN THE TENANT |

Additional fields to collect:

- Existing Conditional Access policies that mention agents or service principals, and exclusions.
- State of Security defaults.
- Confirmation that no tenant-wide emergency policy is enabled without an impact analysis and approval (specification, section 13).
- Whether there are agents that use an API key instead of an Entra token.

General way to obtain: list of policies in the Entra admin centre, sign-in reports filtered by identity type, and a test of each combination with the policy in report-only mode before it is enabled.

## 7. Resources, endpoints, connectors, sessions and alternative paths

### What the official documentation establishes

- Selected scopes need three steps: consent in Entra, assignment on the resource with a role (`read`, `write`, `owner`, `fullcontrol`) and a token with the scope. Without all three, the application has no access. Source: `https://learn.microsoft.com/en-us/graph/permissions-selected-overview`.
- The behaviour of the Selected scope does not change with the Graph path used. Same source.
- Content download returns `302` to a pre-authenticated URL on another host. The URL does not need the `Authorization` header. The validity is "within minutes" on one page and "1 hour" on another. Removing permissions "might not immediately invalidate the URL". Sources: `https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0` and `https://learn.microsoft.com/en-us/graph/api/resources/driveitem?view=graph-rest-1.0`.
- `GET driveItem` returns `@microsoft.graph.downloadUrl` in the metadata. Inference: getting the metadata also hands over the capability to download, unless the executor clears the property.
- `$expand=children` on `GET driveItem` allows children to be listed in the same call. Inference: the allowlist has to cover query parameters.
- `$batch` accepts up to 20 inner requests, accepts `GET` and `DELETE` in the same request, and the outer status `200` does not reflect the inner ones. It exists in v1.0 and in beta. Source: `https://learn.microsoft.com/en-us/graph/json-batching`.
- APIM does not restrict Graph. There is no documentation that allows Graph to be limited to accepting requests only from an APIM. Inference: the real control is the executor holding the credential. Source: `https://learn.microsoft.com/en-us/azure/api-management/secure-api-management` (excerpt) and research 02.
- Whether `/search/query` respects `Sites.Selected` in application mode: NOT VERIFIED in product documentation. There is only a forum answer saying that it does not.
- Target hosts of the `302` in SharePoint Online: NOT VERIFIED. The example on the page is from personal OneDrive.
- Revoking a higher-level scope does not remove the access given by a lower-level scope. Same source as the Selected scopes.
- User sessions: `revokeSignInSessions` invalidates refresh tokens and session cookies, but not access tokens already issued. Source: `https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access`.

### To be filled in the tenant

| Field | General way to obtain | Value |
| --- | --- | --- |
| Pilot site and library (site id, drive id, canonical identifier) | Choice of the site owner, confirmed by a Graph query to the test identity | TO BE FILLED IN THE TENANT |
| Resource for the negative test (another library or another site) | Choice of the site owner | TO BE FILLED IN THE TENANT |
| Endpoints that the agent runtime can reach (network, DNS, egress) | Network rules of the runtime and of the environment | TO BE FILLED IN THE TENANT |
| Connectors and tools linked to the agent (MCP, Power Platform, others) | Inventory of the runtime and of the admin centre | TO BE FILLED IN THE TENANT |
| User sessions open for the identities of the agent | Sign-in reports and active sessions | TO BE FILLED IN THE TENANT |
| Credentials and API keys stored in the runtime or in environment variables | Review of the runtime configuration and of the vault | TO BE FILLED IN THE TENANT |
| Real download target hosts in SharePoint Online | Observation in test traffic | TO BE FILLED IN THE TENANT |
| Behaviour of `/search/query` with Selected | Test on the tenant | TO BE FILLED IN THE TENANT |
| Alternative paths found (direct access, broad grants, shared credentials) | Reconciliation of grants and review of the runtime. Each path lowers the coverage classification until it is eliminated (specification, section 6) | TO BE FILLED IN THE TENANT |

## 8. Permissions, groups, Azure RBAC, local access and shared credentials

### What the official documentation establishes

- Declaring permissions on a blueprint does not grant them. An administrator has to consent, on the blueprint principal or on each agent identity. Source: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity`.
- Blueprints cannot have Azure RBAC roles. They are assigned to the agent identity. Same source.
- Permissions that cannot be granted to agent identities: `User.EnableDisableAccount.All`, `Application.ReadWrite.All`, `Directory.ReadWrite.All`, `AgentIdentityBlueprint.ReadWrite.All`, among others. Source: `https://learn.microsoft.com/en-us/graph/api/resources/agentid-platform-overview?view=graph-rest-1.0`. Consequence: the control identity that disables identities cannot be an agent identity.
- Identifiers of the Selected scopes and the need for administrator consent: `https://learn.microsoft.com/graph/permissions-reference#all-permissions` (excerpt). The Entra role that gives administrator consent: NOT VERIFIED.
- To assign permissions on a site, the documentation requires `Sites.FullControl.All`, and in delegated mode "SharePoint Administrator or higher" plus membership of the site. Sources: `https://learn.microsoft.com/en-us/graph/permissions-selected-overview` and `https://learn.microsoft.com/en-us/graph/api/site-post-permissions?view=graph-rest-1.0`. The documentation acknowledges that the requirement is "necessarily high".
- Exchange: the RBAC for Applications permissions add to those of Entra (union, not intersection). A Graph permission without scope consented in Entra in practice cancels the scope of the RBAC. Source: `https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac`.
- Conditional Access assigned to a group that contains a service principal is not enforced on that service principal. Source: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity`.
- Blueprint credentials are shared by all child identities. A managed identity credential is valid for 90 days and rotates at 45. Sources: Agent 365 identity page and managed identities FAQ.
- Permission changes on managed identities can take several hours to take effect. Source: managed identities FAQ.

### To be filled in the tenant

| Field | General way to obtain | Value |
| --- | --- | --- |
| Application and delegated permissions consented, per pilot identity | Permissions page of each application or agent identity in the Entra admin centre | TO BE FILLED IN THE TENANT |
| Presence of broad grants (`Sites.Read.All`, `Files.Read.All`, `Sites.ReadWrite.All`) on any pilot identity | Same query. Each occurrence is dangerous drift | TO BE FILLED IN THE TENANT |
| Groups that each identity belongs to | Entra admin centre | TO BE FILLED IN THE TENANT |
| Azure RBAC assignments per identity | Access control (IAM) of the resources and subscription in the Azure portal | TO BE FILLED IN THE TENANT |
| Existing Selected grants on the resources (site, list, item, file) | Permission listing endpoint of the resource. Existence and required permission: NOT VERIFIED | TO BE FILLED IN THE TENANT |
| Local access to the resource (site members and owners, shares, links) | Permission settings of the SharePoint site | TO BE FILLED IN THE TENANT |
| Administrative roles assigned and to whom (Agent ID Administrator, Cloud Application Administrator, Exchange Administrator, SharePoint Administrator) | Role assignments in the Entra admin centre | TO BE FILLED IN THE TENANT |
| Credentials of each blueprint (type, number, validity, who holds them) | Blueprint page in the Entra admin centre | TO BE FILLED IN THE TENANT |
| Credentials shared between agents or runtimes (including a reused user-assigned managed identity) | Inventory of identities and runtimes | TO BE FILLED IN THE TENANT |
| Applications with Exchange `Mail.*` consented in Entra (relevant only in the later drafts phase) | List of consented permissions and `Test-ServicePrincipalAuthorization` (documented cmdlet, does not cover Entra grants) | TO BE FILLED IN THE TENANT |

## 9. Retention, residency, backup and recovery

### What the official documentation establishes

- Ledger tables: old data cannot be deleted in append-only tables or in the history of updatable tables, and `TRUNCATE TABLE` is not supported. "Dropped" columns and tables are renamed and stay physically in the database. A ledger database does not go back to being a normal database. Source: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits`.
- Digests: generated every 30 seconds if there are transactions, to Blob (container `sqldbledgerdigests`) or to Confidential Ledger. Source: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management`.
- Container immutability policy: minimum of 1 day and maximum of 146,000 days. Once locked it cannot be deleted and the retention can only increase, at most five times. `allowProtectedAppendWrites` does not change after the lock. Sources: `https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview` and `https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-container-level-worm-policies`.
- The storage account cannot be deleted while there is a container with a locked policy or blobs present. Same sources.
- Immutable Blob is incompatible with point-in-time restore. Same sources.
- Restore and failover: each restore creates a new "incarnation" of the database and verification uses the digests of all incarnations. In a failover group the path of the digests does not change. Digests are only issued for data already replicated to geo-secondaries. Source: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management`.
- Verification requires `ALLOW_SNAPSHOT_ISOLATION` enabled, can consume many resources and the frequency is left to the customer. Elastic Jobs is not supported with Entra-only authentication, so Azure Automation remains as the documented option for scheduling. Sources: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-verification` and `https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication`.
- Confidential Ledger: hard delete with no recovery, 2 ledgers per subscription by default, entry of 8 KiB at most, type (private or public) cannot be changed. Source: `https://learn.microsoft.com/en-us/azure/confidential-ledger/limits`.
- Retention and latency of Agent 365 telemetry: NOT VERIFIED.
- The digest content includes `database_name`, `block_id`, `hash`, `last_transaction_commit_time` and `digest_time`. The blob path includes the server name and the database name. Source: digest management page.

### To be filled in the tenant

| Field | General way to obtain | Value |
| --- | --- | --- |
| Legal and internal retention required for audit events | Retention policy of the organisation and opinion of the data protection officer | TO BE FILLED IN THE TENANT |
| Immutability period to apply before the lock (it can only increase afterwards) | Decision approved by the owner, recorded before the lock | TO BE FILLED IN THE TENANT |
| Data residency requirements and region of the digests | Residency policy. Confirm the effect of replication to the regional pair, if Confidential Ledger is used | TO BE FILLED IN THE TENANT |
| Redundancy of the digest account (not LRS) | Architecture decision | TO BE FILLED IN THE TENANT |
| Pseudonymisation and deletion of personal data outside the ledger | Opinion of the data protection officer | TO BE FILLED IN THE TENANT |
| Approved RPO and RTO | Approval of the service owner | TO BE FILLED IN THE TENANT |
| Redundancy and backup retention of the database | Architecture decision and backup policy | TO BE FILLED IN THE TENANT |
| Mechanism and frequency of ledger verification | Decision: Azure Automation or another, approved | TO BE FILLED IN THE TENANT |
| Retention of logs and telemetry (Log Analytics, Application Insights, Agent 365) | Retention policy. The one for Agent 365 is still to be documented | TO BE FILLED IN THE TENANT |
| Result of the restore and failover rehearsals (BT-17) | Rehearsal in phase C | TO BE FILLED IN THE TENANT |

## 10. Owners and risk approval

### What the official documentation establishes

- Each agent identity and each blueprint requires at least one sponsor. Source: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity`.
- Owners of an agent identity manage their agents without the administrative roles. Source: `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin`.
- Documented roles: Agent ID Administrator or Cloud Application Administrator to manage agent identities, Agent ID Developer to create blueprints, Conditional Access Administrator for policies, Lifecycle Workflows Administrator for lifecycle workflows. Same source.
- For risky actions, the specification (section 13) requires separate roles (reader, incident operator, policy administrator, auditor), temporary privileged access when supported and a break-glass that is independent of the application, documented and rehearsed.
- Reactivation requires a request from one policy administrator and approval from another, by design of the project.

### To be filled in the tenant

| Field | General way to obtain | Value |
| --- | --- | --- |
| Sponsor and owner of each pilot agent identity and blueprint | Object properties in the Entra admin centre and human confirmation | TO BE FILLED IN THE TENANT |
| Incident contacts for each agent | Record kept by the owner | TO BE FILLED IN THE TENANT |
| Holder of the risk approval for the pilot | Decision of the service management | TO BE FILLED IN THE TENANT |
| Holders of the reader, incident operator, policy administrator and auditor functions | Formal assignment of roles | TO BE FILLED IN THE TENANT |
| Procedure and holder of the break-glass, and date of the last rehearsal | Break-glass document and rehearsal record | TO BE FILLED IN THE TENANT |
| Formal acceptance of the risk of each preview feature used | Approval record with a documented alternative | TO BE FILLED IN THE TENANT |
| Approval of the phase A gates: scope, licences, permissions | Review meeting and record of the decision | TO BE FILLED IN THE TENANT |

## 11. What this document blocks

While the fields above are not filled in and approved, the phase A gate does not close. The gaps with the greatest effect, in order of risk:

1. Licence required by each operation and the Conditional Access for agents contradiction C1.
2. Least-privilege permission for reading with Selected, and the permission to grant on a list and on an item. See `permission-matrix.md`.
3. Effect of disabling on tokens already issued, per identity type. See `coverage-matrix.md`.
4. GA or preview status of each feature used and acceptance of the risk of those in preview.
5. Data classification, retention and immutability period, which are irreversible after the lock.
6. Incremental costs and tiers. See `cost-estimate.md`.
