# Gaps and assumptions

Project: Agent Portifolio. Phase A document. Verification date of the Microsoft sources: 2026-11-11.

This document lists what the specification and the official documentation do not settle. It serves the phase A gate: whoever approves must know what remains to be validated and what stays blocked without a tenant.

Project status, without exaggeration: phase A was written from official documentation and without a tenant. Phase B (local vertical slice) is implemented in .NET 8, without real credentials, with the Microsoft world simulated. Phases C, D and E have not been executed. The Bicep is written and has never been applied. There is no real adapter for Entra or for Graph.

## 1. Conventions

Marks used in section 2:

- NOT VERIFIED: the documentation read does not state it. It does not mean it is false.
- CONTRADICTORY: two official pages say different things.
- INFERENCE: conclusion of the project drawn from verified facts. Microsoft does not state it.
- TO BE VALIDATED IN THE TENANT: depends on the tenant and cannot be decided by reading.

Each Microsoft fact carries a key `Mxx` that points to the table of sources at the end of the document (Microsoft Learn URL and date 2026-11-11). Where the "Source" column says "search", the fact comes from a search made without identifying a page and has no URL of its own: in those cases the gap is the lack of a source itself.

The `ms.date` and `updated_at` dates of some pages were read from the metadata. For the Graph, Exchange and APIM pages the converter used did not return metadata, so the last-update date of those pages is NOT VERIFIED.

## 2. Gaps in the Microsoft documentation

Each row says what is missing or contradicted, and the impact on the project.

### 2.1 Identity, revocation and tokens

| Id | State | What the documentation says or omits | Impact on the project | Source |
|---|---|---|---|---|
| L-01 | NOT VERIFIED | Entra Agent ID "is available for all Microsoft Entra customers", but the text read never declares it GA. The Graph APIs for agent identity are in `v1.0`. Inferring production readiness from `v1.0` is not a statement by Microsoft. | ADR 0009 cannot state GA. Accepting the risk of depending on Agent ID is a human decision. | M60, M18 |
| L-02 | NOT VERIFIED | No example shows the request body that disables an agent identity or a blueprint principal. The `accountEnabled` property exists and the minimum permission is called `AgentIdentity.EnableDisable.All`, which makes `{"accountEnabled": false}` likely (inference). | The real disable adapter cannot be written as fact. Validate in the tenant in phase D. | M17, M18 |
| L-03 | CONTRADICTORY | The Graph reference describes `accountEnabled` as "no users are able to sign in to this app". The administration pages speak of "block its access and token issuance". | The Graph reference does not confirm blocking of app-only tokens. Measure in the tenant. | M18, M06 |
| L-04 | NOT VERIFIED | No page read says what happens to access tokens already issued to an agent identity after it is disabled. All the language is about future issuance. | This is the core of BT-14. The project assumes validity until expiry and measures the real result. See risk R-02. | M05, M06, M13 |
| L-05 | NOT VERIFIED | The cascade effect of disabling the agent identity on the associated agent user. The documentation says the agent user "can only authenticate by presenting a token issued to the associated agent identity", which suggests a dependency without stating it. | Revoking an agent that uses all three modes (application, on behalf of the user, agent user) may require acting on three token subjects. | M21, M05 |
| L-06 | NOT VERIFIED | Whether blocking in the Microsoft 365 admin centre (Agent 365) changes `accountEnabled` in Entra, or whether they are two independent switches. The three blocking pages were read only as excerpts. | There are possibly two control planes with no documented synchronisation. The adapter and the coverage report have to treat them separately. | M64 |
| L-07 | NOT VERIFIED | "Disable in all tenants" exists for multitenant blueprints in the admin centre. The Graph endpoint and the equivalent property are not documented in the pages read. | Outside the single-tenant pilot. Any action on a multitenant blueprint stays without automation until there is a source. | M05 |
| L-08 | CONTRADICTORY | One page speaks of disabling the "agent identity blueprint". Another says the same list shows blueprint principals and calls the action "Disable an agent identity blueprint principal". The `EnableDisable` permission exists for the principal and there is no equivalent for the blueprint. | It is not clear whether the tenant-local disable acts on the application or on the service principal. ADR 0002 treats the principal as the likely target (inference). | M05, M20 |
| L-09 | NOT VERIFIED | CAE for agents: the only mention is "Some resources also support Continuous Access Evaluation". No list of resources or events, and no statement on whether disabling an agent identity is a revocation event. | Do not presume near real-time revocation for agents. See R-02. | M04 |
| L-10 | CONTRADICTORY | CAE for workload identities says "instantly" in the introduction and gives no numeric value. The critical events for users indicate "up to 15 minutes". CAE for workload identities covers only Microsoft Graph and single-tenant service principals of line-of-business applications, requires `cp1` and excludes managed identities. | Do not promise instant revocation. No numeric deadline for service principals. | M02, M03 |
| L-11 | CONTRADICTORY | Default access token lifetime: "1 hour" on two pages, "60-90 minutes (75 minutes on average)" on two, "two hours" for Teams and Microsoft 365 without Conditional Access. The `access-tokens` page does not apply to Microsoft APIs such as Graph. Lifetime for agent identities: NOT VERIFIED. | Use 90 minutes as the worst case without CAE. With CAE, 28 hours (users) and 24 hours (workload identities). | M01, M02, M07, M08 |
| L-12 | NOT VERIFIED | Tokens already issued after `accountEnabled=false` on a service principal: the page says it prevents issuance and says nothing more. Only the CAE page for workload identities links "service principal disable" to existing tokens, and only under the restricted conditions. For disabling an application (`isDisabled`) the text is explicit: existing tokens remain valid until they expire. | By analogy (inference), tokens remain until they expire. Measure in the tenant. | M11, M12, M03 |
| L-13 | NOT VERIFIED | Whether `accountEnabled=false` is supported and effective on the service principal of a managed identity. There is no equivalent disable procedure for managed identities. The token cache lasts about 24 hours without a forced refresh, and tokens of a deleted identity remain valid to the end. | The managed identity has the longest revocation window and the fewest documented controls. The documentation recommends federating the blueprint to a managed identity, and the effect of the cache on revoking a federated blueprint is NOT VERIFIED. See R-03. | M09, M10, M14 |
| L-14 | CONTRADICTORY | Licence for Conditional Access applied to agents: one page requires Entra ID P1, the other (more recent) requires Microsoft 365 E7 or Agent 365 plus Entra P1 or Microsoft 365 E3. ID Protection for agents: P2 during the preview, and "starting soon" Agent 365. Prices: NOT VERIFIED (the pages point outside Learn). | Record Agent 365 as a requirement until proven otherwise. Do not write values. See R-04. | M04, M05, M06, M62 |
| L-15 | NOT VERIFIED | The Conditional Access policies for agents and the agent sign-in logs use the `beta` endpoint in the examples. Whether the conditions `includeAgentIdServicePrincipals`, `AllAgentIdUsers` and `AllAgentIdResources` exist in `v1.0`: NOT VERIFIED. Whether a workload identities policy with `ServicePrincipalsInMyTenant` catches agent identities: NOT VERIFIED. | Any tenant-wide containment automation by policy depends on a beta API. See ADR 0009. | M05, M06, M10 |
| L-16 | CONTRADICTORY | The agent user is "available only to tenants participating in the Frontier preview program" in the Agent 365 context. The Entra pages and the `agentUser` API in `v1.0` do not mention preview. | Likely (inference): the object exists in `v1.0`, and the Microsoft 365 capabilities depend on Frontier. The MVP uses only application mode. | M14, M21 |
| L-17 | NOT VERIFIED | Retention and latency of Agent 365 telemetry, numeric value of the rate limit, read API (`Agent365.Observability.OtelRead` has no documented API in the pages read), coverage of agents without instrumentation, availability in sovereign clouds. A `200 OK` does not prove ingestion (spans rejected for lack of licence). | Agent 365 observability cannot be the primary evidence. It stays complementary to the ledger. | M15, M16 |
| L-18 | NOT VERIFIED | Entra administrative role needed to give administrator consent to application permissions. The consent documentation was not opened. | Field of the permission matrix still to be filled in. | M22 |

### 2.2 SharePoint and Microsoft Graph

| Id | State | What the documentation says or omits | Impact on the project | Source |
|---|---|---|---|---|
| L-19 | CONTRADICTORY | The Selected permissions overview says that the Selected scopes give access to resources through any Graph path, with the `read` role for reading metadata and content. None of the four read pages (list libraries, list children, get metadata, download) lists a `*.Selected` scope in the application permissions table: the lowest is `Files.Read.All`, with whole-tenant scope. | This is the most important gap for the MVP. The design assumes `Sites.Selected` with the `read` role (inference from the overview). It has to be confirmed endpoint by endpoint in the tenant before any statement. Never use `Files.Read.All` as a workaround. See R-07. | M22, M25, M26, M27 |
| L-20 | CONTRADICTORY | Who can assign permissions on a list and an item: the overview requires `Sites.FullControl.All` or `Sites.Selected` with the FullControl or Owner role. The API reference gives `Sites.ReadWrite.All` as the least privilege and does not list Selected scopes. | The design assumes the stronger requirement (overview) until tested. The identity that assigns permissions is separate from the executor. | M22, M24 |
| L-21 | CONTRADICTORY | Creating a permission on the site accepts delegated mode, removing a permission on the site does not ("Not supported."). | Revoking at site level requires an application identity with `Sites.FullControl.All`, or revoking consent in Entra. This affects the design of the grant-removal adapter. | M23, M66 |
| L-22 | NOT VERIFIED | GA or preview state of each Selected level: the words "GA" and "preview" do not appear. The assignment API is in `v1.0` for site, list and item without a beta notice. The v1.0 assignment page on `driveItem` was not read in full, and the removal pages on list and item appeared only in `beta`. | See ADR 0009. It may force the use of `beta` for revocation at list or item level. | M22, M24, M67 |
| L-23 | NOT VERIFIED | Listing and getting the permissions of a site (`GET .../permissions`). The removal page on item writes a path without the `/permissions/` segment, probably a page error (inference). | Reconciliation needs to list what is assigned. Without this read confirmed, T-11 and T-12 are not closed. | M22 |
| L-24 | NOT VERIFIED | Whether `/search/query` respects `Sites.Selected`. The only source is a forum answer (Microsoft Q&A) that says it does not. It is not product documentation. | The MVP does not use search. Any future search operation stays blocked until proven. | search |
| L-25 | CONTRADICTORY | Validity of the pre-authenticated download URL: "might expire within minutes" on one page, "(1 hour)" on another, and "removing file permissions for a user might not immediately invalidate the URL". Destination hosts of the 302 in SharePoint Online: NOT VERIFIED (the example is from personal OneDrive). | Worst case of 1 hour. The allowed-host list in the executor is still to be defined in the tenant. See R-08. | M25, M28 |
| L-26 | NOT VERIFIED | Size limit of the `$batch` body. The page points to "known issues" outside Learn, which was not read. | The gateway policy denies `$batch` by default and this limit is no longer needed in the MVP. | M29 |
| L-27 | CONTRADICTORY | `RateLimit` headers in SharePoint: recommended in the best-practice list and declared "not return or support" in the dedicated section of the same page. | The project depends only on `Retry-After`. | M31 |

### 2.3 Exchange (later phase, outside the MVP)

| Id | State | What the documentation says or omits | Impact on the project | Source |
|---|---|---|---|---|
| L-28 | NOT VERIFIED | GA state of RBAC for Applications. The page says neither "preview" nor "GA". The contact address is called `exoapprbacpreview@microsoft.com` and the consolidated view in Entra "is upcoming". | Record as a preview risk until confirmed. See ADR 0009. | M32 |
| L-29 | CONTRADICTORY | RBAC for Applications "replaces" the Application Access Policies, but only these restrict permissions granted in Entra. No announced deprecation date for the legacy policies. | Whoever has broad permissions in Entra and wants to limit them has to remove them. Do not use the legacy feature in new implementations. | M32, M33 |
| L-30 | NOT VERIFIED | There is no application role for drafts only. The closest is `Application Mail.ReadWrite`, which allows reading, changing and deleting mail within the scope, without sending. The practical difference between `Mail.ReadWrite` and `Mail-Advanced.ReadWrite.All` is not explained. | The restriction to drafts is done in the executor, not in Exchange. The external permission is broader than the capability. | M32 |
| L-31 | CONTRADICTORY | RBAC permission assignment adds to those of Entra ("union"). Change propagation takes between 30 minutes and 2 hours, and the test cmdlet ignores the cache and excludes Entra permissions. | A positive or negative cmdlet test does not prove the effective state. Mail revocation is not immediate. | M32 |

### 2.4 APIM, ledger, digests and infrastructure

| Id | State | What the documentation says or omits | Impact on the project | Source |
|---|---|---|---|---|
| L-32 | NOT VERIFIED | No documentation found that allows restricting Microsoft Graph to accept requests only from an APIM. The network mechanisms (virtual network, private endpoint, IP list, mTLS) apply to the APIM to backend leg. | Bypassing the gateway by someone who holds a Graph credential is not prevented by the network. The real control is that the agent does not possess a credential. See R-06 and ADR 0003. | M55, M38 |
| L-33 | NOT VERIFIED | The APIM "AI gateway" governs traffic of models and of MCP and A2A tools. It has no documented capability for Graph, SharePoint or Exchange semantics, nor for `$batch` or 302 redirects. Availability of the capabilities varies by APIM tier. | Fine-grained authorisation of operations remains to be built. APIM does not turn it into that automatically. | M34 |
| L-34 | NOT VERIFIED | The `validate-azure-ad-token` policy only requires `tenant-id`. The signing-key cache behaviour is documented only for `validate-jwt`. The default of `authentication-endpoint` appears with a hyphen (`login-microsoftonline.com`), probably a typo. The full list of `validate-jwt` attributes was not read in full. | The policy template has to enforce `audiences` and client application. The behaviour of `validate-azure-ad-token` when Entra fails remains to be validated. | M35, M36 |
| L-35 | NOT VERIFIED | Literal phrase "generally available" for SQL Ledger in Azure SQL Database. The pages show "Applies to" without a preview mark. Number and text of the error on `UPDATE` or `DELETE` on an append-only table: only in an image. Columns of `sys.database_ledger_blocks`. Transaction size limit (only the limit of 200 tables per transaction). | See ADR 0004 and 0009. BT-16 tests cannot assert the error text. | M42, M43, M44, M63 |
| L-36 | NOT VERIFIED | Digests: whether a user-assigned identity of the SQL server can do the upload (the documentation speaks of "System Identity"); behaviour with a private network or firewall on the storage account; whether the `sqldbledgerdigests` container can be pre-created with the policy before the first upload; whether the policy `lock` can be declared in Bicep; names of the upload metrics. | The phase C Bicep may not be able to declare everything. The policy lock is treated as a manual, irreversible step. | M41, M45, M46, M58 |
| L-37 | CONTRADICTORY | With Entra-only authentication, Elastic Jobs is not supported, and it is the option that the ledger documentation cites for scheduling verification (alongside Azure Automation). | Scheduling of verification uses Azure Automation or a custom job (a project choice, not in the documentation). | M40, M53 |
| L-38 | NOT VERIFIED | Azure Confidential Ledger: Bicep type and `apiVersion`, nature of the cost, and the comparison table with immutable Blob that the digests page mentions. Verified limits: 2 ledgers per subscription, 8 KiB per entry, replication to the regional pair with no option to disable it. | Confidential Ledger stays as an option, not as the baseline. See ADR 0004. | M41, M48, M49 |
| L-39 | NOT VERIFIED | `sku` values of SQL Database and Service Bus in the Bicep reference, `workloadProfileType` values, format of the `identity` block of Container Apps, `accessPolicies` when `enableRbacAuthorization` is true. Real acceptance of the 2026 `apiVersion` values by the Bicep CLI and by the regions. | The Bicep is written and has never been compiled against a subscription. Treat as not validated. | search |
| L-40 | NOT VERIFIED | Service Bus: availability of sessions by tier, whether `requiresSession` can change after the queue is created. Duplicate detection covers sending, not redelivery to the consumer. | Message order cannot be assumed. The design uses epoch and idempotency. See ADR 0005. | M56, M51, M50 |
| L-41 | NOT VERIFIED | Exact `GRANT` syntax for `ENABLE LEDGER`, `VIEW LEDGER CONTENT` and `GENERATE LEDGER DIGEST`. Nature of the cost of the ledger feature, of Key Vault per operation, of API Management per tier and of managed identities. | The cost estimate (`cost-estimate.md`) can only describe the nature of the cost and point to the official pricing page. No values. | search |
| L-42 | NOT VERIFIED | Last-update dates of all Graph, Exchange and APIM pages. Pages read only as excerpts: Agent ID lifecycle policies, Entra licensing, ID Governance for agents, blocking in Agent 365, and three managed identity examples in other services. | Before publishing any of these facts, read the page in full. | M61, M62, M64 |

## 3. Design assumptions

Each assumption says what the design assumes, where it appears in the code or in the documents, and how it is validated. None has been validated: there is no tenant.

| Id | Assumption | Where it appears | How it is validated | If it fails |
|---|---|---|---|---|
| P-01 | The site identifier has the form `host,GUID,GUID`, with a lowercase host and lowercase GUIDs. Drive and item identifiers are opaque text with `A-Za-z0-9!_-`, up to 256 characters. The canonical resource form is `sharepoint:site/{siteId}/drive/{driveId}`. | `ResourceCanonicalizer` and `OperationCatalog` in `src/Core/Domain/Operations.cs`. The regular expressions are closed. | TO BE VALIDATED IN THE TENANT: obtain real identifiers of the pilot site and library and run the resolver. | The resolver rejects real identifiers (fails closed, blocks use). Adjust the expression and repeat TC-07. |
| P-02 | The agent token in application mode carries `tid` (tenant) and `oid` (object identifier of the service principal of the agent identity), and `oid` equals the `Identity.ObjectId` of the registered agent. | `CallerIdentity(TenantId, ObjectId)` and `FindAgentByIdentityAsync`. | TO BE VALIDATED IN THE TENANT: obtain a real token of a test agent identity, decode it and compare. The documentation read does not list the claims of these tokens. | The identity mapping fails, the agent is not resolved (`agent_unknown`). Adjust the claim used and repeat BT-02. |
| P-03 | The agent uses application mode (the agent identity is the token subject). The on-behalf-of-user and agent user modes are not part of the MVP. | Initial scope of the specification, section 2. | Scope decision in phase A. | Without the decision, revocation needs to cover three token subjects (L-05). |
| P-04 | A valid tenant token is not enough to pass APIM: the policy template enforces `audiences` and client application (or `required-claims`). | APIM policy template of phase C. | Phase C: a token with the wrong audience and with the wrong application gets 401 (TC-02). | Any tenant token passes the gateway. |
| P-05 | `Sites.Selected` with the `read` role is enough for the four read operations (list libraries, list children, get metadata, download), even though the reference tables list `Files.Read.All`. | Permission matrix and `IDocumentGateway`. | Phase C, endpoint by endpoint, with a negative test on another resource. | The MVP is no longer viable with narrow scope. Stop and decide. Never use `Files.Read.All`. See R-07. |
| P-06 | Assigning permissions on lists and items requires `Sites.FullControl.All` (stronger requirement), and this identity is different from the executor. | Plan of the grant adapter (phase C). | Phase C: try with `Sites.ReadWrite.All` and confirm the result (decides L-20). | If less were enough, the requirement is relaxed. If none of what was foreseen were enough, the grant design changes. |
| P-07 | `accountEnabled=false` on an agent identity prevents issuance of new tokens for that identity, and the request body is `{"accountEnabled": false}` on the endpoint `PATCH /servicePrincipals/{id}/microsoft.graph.agentIdentity`. | Entra disable adapter (simulated today). | Phase D, with a test agent identity. | External containment does not block issuance. The global state does not become `VerifiedScoped`. |
| P-08 | A token issued before the disable can be kept and presented again on a test resource, and its result can be measured. | BT-14 and the old-token probe (simulated today). | Phase D, by identity type and by resource. | Without this measurement no statement is possible about tokens already issued. |
| P-09 | The executor can follow the 302 of `/content` and download without an `Authorization` header, for a finite set of hosts that the outbound list can enumerate. | `IDocumentGateway` (simulated today). | TO BE VALIDATED IN THE TENANT, phase C: observe the real SharePoint Online hosts. | The host list has to be widened, or the download fails closed. |
| P-10 | Each agent has its own execution identity (`ExecutorObjectId`) with a Selected grant on the resource, and managed identities are not shared between runtimes with incompatible scopes. | `Agent` model and design rules 9 and 10. | Phase C, with the coverage matrix. | Low coverage, `DirectAccessPossible`. |
| P-11 | The ledger is table by table (`WITH (LEDGER = ON (APPEND_ONLY = ON))` on `ap.AuditEvent`), and the database is not a ledger database (`isLedgerOn` off), because the remaining tables are updatable. | `Storage.SqlServer` and Bicep (not yet applied). To be confirmed in the Bicep. | Human decision in phase A. The property is irreversible once the database is created. | If the database were a ledger database, all tables, including the operational ones, would become ledger tables. See ADR 0004. |
| P-12 | SQL Server 2022 locally, in docker, behaves like Azure SQL Database for the append-only ledger and for verification (except digest in independent storage). | Local BT-16, executed against SQL Server 2022 in a container (`BT16_`, 2026-11-11). | Phase C: repeat BT-16 against Azure SQL with digests in Blob. | Differences appear only in phase C. |
| P-13 | The destination of the digests is immutable Blob with a `locked` policy and redundancy other than LRS, and the retention is decided before the lock. | Bicep (not yet applied). | Human retention decision and phase C. | After the lock the retention can only increase. |
| P-14 | The outbox in the database is the source of delivery. The target transport for phase C is Service Bus, and the local `ContainmentWorker` reads the outbox directly. | Design rule 4 and ADR 0005. | Phase C, BT-09 and BT-10 against real Service Bus. | Real delivery behaviour (redelivery, dead-letter, duplicate window) has not been exercised. |
| P-15 | The interval of the periodic reconciliation will be defined by the owner, and reconciliation has read permissions in Entra and on the resource. | `ReconciliationService`. | Decision in phase A, validation in phase C. | Drift goes undetected during the interval. |
| P-16 | The 5-second time to refuse new admissions after a persisted suspension is an initial objective, measured in the pilot environment, not a promise. | SLO in section 15 of the specification. | Phase C. | If the objective fails, the measured value is described. |
| P-17 | The object identifiers of operators and owners stored in events may be personal data. Pseudonymisation, retention and deletion are decided before immutability is locked. | Ledger events and retention policy. | Human decision, with the data protection officer. GDPR compliance is not declared for using Microsoft services. See R-05. | Personal data becomes immutable. |
| P-18 | There is a single pilot tenant, one Azure runtime controlled by the team, one SharePoint read agent and one authorised library, with a negative test on another resource. | Initial scope of the specification, section 2. | Scope approval in phase A. | Without approved scope the gate does not pass. |
| P-19 | Role segregation (Reader, IncidentOperator, PolicyAdmin, Auditor) maps to Entra app roles with the same names. | `Roles` in `src/Core/Domain/Vocabulary.cs`. | Phase C, with the tenant. | The roles do not reach the token, and the ControlApi denies everything. |
| P-20 | Break-glass is independent of the application, documented and rehearsed. | BT-20, runbooks. | Phase D, real rehearsal. | No emergency access with the application down. |
| P-21 | The default external timeout limit (10 seconds) and the per-hour limit per agent are start-up values in the code, not calibrated values. | `ExecutionOptions.ExternalTimeout` (configurable through `Execution__ExternalTimeoutSeconds`) and the `maxActionsPerHour` field of the agent. | Measure in phase C. | False `Unknown` results from a short timeout, or an unsuitable limit. |

## 4. Milestones blocked by phase

What does not advance without a tenant or without a human decision. The specification (instruction 7) orders blocking the corresponding milestone, not hiding the gap with mocks.

### 4.1 Phase A: validation

State: documents written. Gate pending human approval: approval of scope, licences and permissions.

| Milestone | Blocked by | Unblocking |
|---|---|---|
| Tenant fields in `environment-assessment.md` (licences, runtimes, permissions, retention, residency) | There is no tenant. They stay "TO BE FILLED IN THE TENANT". | Access to the pilot tenant. |
| Complete permission matrix (administrative role, licence per operation) | Licence per operation NOT VERIFIED (L-14). Consent role NOT VERIFIED (L-18). | Test in tenant, and confirmation of the licences by the owner. |
| Acceptance of preview dependencies | Human decision (ADR 0009). | Formal approval. |
| Retention, pseudonymisation and deletion of data in the ledger | Human decision, with data protection (P-17). | Decision before the immutability lock. |
| Assignment of owners of threats and risks | Decision of the project owner. | Name the roles. |

### 4.2 Phase B: local slice

State: implemented. Without real credentials. Gate: invariants and concurrency tested, pending human approval.

| Milestone | Blocked by | Unblocking |
|---|---|---|
| BT-05, BT-17, BT-20 | They need real resources. The local approximation does not count as proof. BT-20 has no test. | Phases C and D. |
| BT-06, BT-13, BT-14 | They exist only in simulation. They do not count as proof. | Phases C and D. |
| Local TC tests (TC-01, 04 to 10, 14, 18) | Not blocked. TC-08, TC-09, TC-14 and TC-18 exist. TC-01, TC-04, TC-05, TC-06 and TC-07 exist in part. TC-10 still to be implemented. | Write and run locally. See `test-plan.md`. |

### 4.3 Phase C: Azure test

State: not executed. Bicep written, never applied.

| Milestone | Blocked by | Unblocking |
|---|---|---|
| Any Azure resource | No subscription, no tenant, no approval of costs and tiers. | Test subscription and gate A approved. |
| Permission matrix: endpoint-by-endpoint validation | L-19, L-20, L-21 (Selected contradictions). | Test in tenant with an authorised resource and a negative resource. |
| Agent `tid` and `oid` claims | P-02. | Real token of a test agent identity. |
| Format of site, drive and item identifiers | P-01. | Real identifiers. |
| 302 hosts in SharePoint Online | L-25, P-09. | Observation in the tenant. |
| APIM policy (`validate-azure-ad-token`, deny `$batch`) | APIM tier and availability of the capabilities (L-33). | Tier decision and Bicep compilation. |
| SQL Ledger and digests with `locked` policy | Decision on `isLedgerOn` (P-11), retention (P-13), manual lock (L-36). | Human decisions and subscription. |
| Service Bus (real BT-09, BT-10) | Subscription and tier decision. Sessions by tier NOT VERIFIED (L-40). | Subscription. |
| BT-05 and BT-06: proof of impossibility of direct access | All of the above. | Phase C gate. |
| BT-17: restore and failover | Real Azure SQL with geo-replication. | Phase C. |

### 4.4 Phase D: containment

State: not executed. No real adapter for Entra or for Graph.

| Milestone | Blocked by | Unblocking |
|---|---|---|
| Real Entra disable adapter | Request body NOT VERIFIED (L-02), ambiguous `accountEnabled` (L-03). | Test agent identity and Agent ID Administrator role. |
| Real removal of Selected grants | Asymmetric assignment and removal (L-21), v1.0 on list and item NOT VERIFIED (L-22). | Test in the tenant. |
| BT-14: previously issued token | L-04, L-11, L-12, L-13. | Measurement by identity type and by resource. |
| Real BT-13: blueprint siblings | L-05, L-08. | Tenant with a blueprint and two agent identities. |
| Managed identities: isolation without disable | L-13. | Tenant, runtime and test managed identity. |
| Relationship between blocking in Agent 365 and Entra | L-06. | Test in the tenant, if Agent 365 is licensed. |
| Agent user | L-16 and Frontier programme. | Outside the MVP. |
| BT-20: break-glass | Independent runbook (P-20). | Real rehearsal. |
| Scoped mail (drafts) | L-28, L-30, L-31. Outside the MVP. | Decision to enter the later phase. |

### 4.5 Phase E: pilot

State: not executed. Gate: human production approval, zero critical risks that are unmitigated or not formally accepted.

| Milestone | Blocked by | Unblocking |
|---|---|---|
| Load test | Load tests are not permitted on SharePoint Online [M31]. | A plan that dispenses with load on real SharePoint. |
| Review of security, recovery, retention, costs, operation | Phases C and D completed. | Previous gates. |
| Production approval | All risks in `risks.md` decided (accept, mitigate or block). | Human decision. |

## 5. Sources

All verified on 2026-11-11. Prefix: `https://learn.microsoft.com/en-us/`. "Read" indicates whether the page was read in full or only as a search excerpt.

| Key | Path | Read |
|---|---|---|
| M01 | `entra/identity/users/users-revoke-access` | full |
| M02 | `entra/identity/conditional-access/concept-continuous-access-evaluation` | full |
| M03 | `entra/identity/conditional-access/concept-continuous-access-evaluation-workload` | full |
| M04 | `entra/identity/conditional-access/agent-id` | full |
| M05 | `entra/agent-id/disable-agent-identities` | full |
| M06 | `entra/agent-id/manage-agent-identities-admin` | full |
| M07 | `entra/identity-platform/configurable-token-lifetimes` | full |
| M08 | `entra/identity-platform/access-tokens` | full |
| M09 | `entra/identity/managed-identities-azure-resources/managed-identities-faq` | full |
| M10 | `entra/identity/conditional-access/workload-identity` | full |
| M11 | `entra/identity/enterprise-apps/disable-user-sign-in-portal` | full |
| M12 | `entra/identity/enterprise-apps/deactivate-app-registration` | full |
| M13 | `entra/agent-id/key-concepts` | full |
| M14 | `microsoft-agent-365/developer/identity` | full |
| M15 | `microsoft-agent-365/developer/observability-concepts` | full |
| M16 | `microsoft-agent-365/overview` | full |
| M17 | `graph/api/agentidentity-update?view=graph-rest-1.0` | full |
| M18 | `graph/api/resources/agentidentity?view=graph-rest-1.0` | full |
| M20 | `entra/agent-id/manage-agent-blueprint` | full |
| M21 | `entra/agent-id/agent-users` | full |
| M22 | `graph/permissions-selected-overview` | full |
| M23 | `graph/api/site-post-permissions?view=graph-rest-1.0` | full |
| M24 | `graph/api/list-post-permissions?view=graph-rest-1.0` | full |
| M25 | `graph/api/driveitem-get-content?view=graph-rest-1.0` | full |
| M26 | `graph/api/driveitem-get?view=graph-rest-1.0` | full |
| M27 | `graph/api/driveitem-list-children?view=graph-rest-1.0` | full |
| M28 | `graph/api/resources/driveitem?view=graph-rest-1.0#instance-attributes` | excerpt |
| M29 | `graph/json-batching` | full |
| M31 | `sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online` | full |
| M32 | `exchange/permissions-exo/application-rbac` | full |
| M33 | `exchange/permissions-exo/application-access-policies` | full |
| M34 | `azure/api-management/genai-gateway-capabilities` | full |
| M35 | `azure/api-management/validate-azure-ad-token-policy` | full |
| M36 | `azure/api-management/validate-jwt-policy` | excerpt |
| M38 | `entra/msidweb/advanced/api-gateways#follow-best-practices` | excerpt |
| M40 | `sql/relational-databases/security/ledger/ledger-database-verification` | full |
| M41 | `sql/relational-databases/security/ledger/ledger-digest-management` | full |
| M42 | `sql/relational-databases/security/ledger/ledger-limits` | full |
| M43 | `sql/relational-databases/security/ledger/ledger-database-ledger` | full |
| M44 | `sql/relational-databases/security/ledger/ledger-append-only-ledger-tables` | full |
| M45 | `sql/relational-databases/security/ledger/ledger-monitor-digest-uploads` | full |
| M46 | `azure/storage/blobs/immutable-storage-overview` | full |
| M48 | `azure/confidential-ledger/overview` | full |
| M49 | `azure/confidential-ledger/limits` | full |
| M50 | `azure/service-bus-messaging/service-bus-message-loss-and-duplicates` | excerpt |
| M51 | `azure/service-bus-messaging/duplicate-detection` | full |
| M53 | `azure/azure-sql/database/authentication-azure-ad-only-authentication` | full |
| M55 | `azure/api-management/secure-api-management#network-security` | excerpt |
| M56 | `azure/service-bus-messaging/message-sessions` | excerpt |
| M58 | `sql/relational-databases/security/ledger/ledger-how-to-enable-automatic-digest-storage` | full |
| M60 | `entra/agent-id/what-is-microsoft-entra-agent-id` | full |
| M61 | `entra/id-governance/agent-lifecycle-policies` | excerpt |
| M62 | `entra/fundamentals/licensing` | excerpt |
| M63 | `azure/azure-sql/database/ledger-landing` | full |
| M64 | `microsoft-agent-365/admin/manage-agents` | excerpt |
| M66 | `graph/api/site-delete-permission?view=graph-rest-1.0` | excerpt |
| M67 | `graph/api/driveitem-post-permissions?view=graph-rest-beta` | full |
