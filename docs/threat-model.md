# Threat model

Project: Agent Portifolio. Phase A document, section 14 of the specification. Verification date of the Microsoft sources: 2026-11-11.

Project status, without exaggeration: phase A (validation) was written from official documentation and without a tenant. Phase B (local vertical slice) is implemented in .NET 8, without real credentials, and the Microsoft world is simulated. Phases C, D and E have not been executed. The Bicep is written and has never been applied. There is no real adapter for Entra or for Graph. Everything that depends on the tenant is marked "TO BE VALIDATED IN THE TENANT".

## 1. Conventions

Every statement about Microsoft carries an `Mxx` key that points to the source table at the end of the document, with the Microsoft Learn URL and verification date 2026-11-11.

Three levels of certainty, always kept separate:

- Verified in documentation: the official text states it.
- Inference: a conclusion of this project drawn from verified facts. Microsoft does not state it.
- To be validated in the tenant: it cannot be decided without a tenant. None of this is treated as fact.

"NOT VERIFIED" means that the documentation read does not state it. It does not mean that it is false.

Test identifiers:

- `BT-01` to `BT-20` are the blocking tests from section 15 of the specification, in the same order. List in section 5 of this document.
- `TC-nn` are tests that the specification does not cover. The number `nn` is the threat number (T-nn). List in section 6. The local `TC` tests exist in part, as each threat says. The phase C and D ones do not exist.

Local status of the BT tests, confirmed against the suite on 2026-11-11 (78 tests, 78 passed with `AP_TEST_SQL` set against a real SQL Server 2022, 73 passed and 5 skipped without it): BT-05, BT-17 and BT-20 are only proven in phases C and D. BT-06, BT-13 and BT-14 exist only in simulation. Locally there is at most a simulated approximation, which does NOT count as proof. The others have local proof, over the simulated world, and BT-08, BT-10 and BT-16 also have a test against a real SQL Server 2022. The detail, with the test names, is in `test-plan.md`.

Component names follow the code in `src/Core` (`AuthorizationEngine`, `OperationCatalog`, `ResourceCanonicalizer`, `ExecutionService`, `IWalletStore.TransactAsync`, `IDocumentGateway`, `IContainmentAdapter`, `ReasonCodes`, `EventTypes`). `ControlService`, `ContainmentOrchestrator` and `ReconciliationService` now exist in `src/Core/Services`.

Owners: none has been assigned. Each threat states "to be assigned" with the suggested role. The assignment is a decision for the project owner and is part of the phase A gate.

## 2. Trust boundaries

Source: section 5 of the specification. Boundary F8 is an inference from section 13 (separate roles, IaC, privileged access) and is not in the list in section 5.

| Id | Boundary | Who is on each side | Design rule |
|---|---|---|---|
| F1 | Agent to gateway | Agent and runtime (untrusted) against APIM | The agent is not trusted for authorisation, declared identity or audit. The caller comes from a validated token. |
| F2 | Gateway to executor | APIM against PolicyExecutor | The backend must not be bypassable. The executor also validates the token and reads the authoritative state. |
| F3 | Executor to Microsoft Graph | PolicyExecutor against Graph and SharePoint | The data credential stays in the executor. The agent never receives it. One execution identity per agent. |
| F4 | Operator to control API | People against ControlApi | Entra authentication and roles Reader, IncidentOperator, PolicyAdmin, Auditor. The control identity is not used by the agent runtime. |
| F5 | Services to SQL | ControlApi, PolicyExecutor and ContainmentWorker against SQL | State, policies, outbox and ledger. All queries carry the tenant. A single transactional port. |
| F6 | Worker to the outside world | ContainmentWorker against Entra, runtime and resources | Idempotent adapters, with timeout, bounded retries and a verification probe. |
| F7 | SQL to digests | Database against independent digest storage | Digests are only worth something if the destination cannot be tampered with by those who hold privileges on the database [M41]. |
| F8 | Management plane | Those who edit Azure RBAC, APIM policies, Bicep and CI against everything else | Outside the application. The application cannot defend itself against whoever controls the platform. Only the ledger and the digests give evidence. |

Text diagram:

```text
 UNTRUSTED                    |            TRUSTED (controlled by the team)
                              |
 [Agent + runtime]            |
        |  Entra token        |
        v                     |
       (F1)                   |
        |                     |
        +-------------------> [APIM] --(F2)--> [PolicyExecutor] --(F3)--> [Microsoft Graph / SharePoint]
                              |                       |                     (credential only here;
                              |                       | (F5)                 302 to pre-authenticated URL
                              |                       v                      on another host)
 [Operator] --Entra--(F4)---> [ControlApi] --(F5)--> [SQL: state, outbox, ap.AuditEvent (ledger)]
                              |                       |                |
                              |                       | outbox         | (F7) digests
                              |                       v                v
                              |              [ContainmentWorker]   [Immutable storage]
                              |                       |
                              |                       +--(F6)--> [Entra | runtime | grants on the resource]
                              |
 F8: management plane (Azure RBAC, APIM policies, Bicep, CI) crosses all trusted blocks
```

Two consequences that the diagram makes it necessary to keep in mind.

First: the final destination of the data traffic is Graph, a public Microsoft service. The documented network mechanisms for APIM (virtual network, private endpoint, IP list, mTLS) apply to the gateway to executor leg [M55]. The documentation read shows no way to restrict Graph to accept requests only from one APIM (inference). What prevents bypassing the gateway is that the agent does not hold a Graph credential.

Second: the content of a downloaded file follows a 302 redirect to a pre-authenticated URL on another host [M25]. That traffic does not pass through APIM when the caller is the one following it. For that reason it is the executor that follows the redirect.

## 3. Threats

Each entry has: boundary, precondition, mitigation (what the design does, with the component), test, evidence, residual risk and owner.

### T-01 Prompt injection

- **Boundary:** F1 (the agent is untrusted), with effect on F2 and F3.
- **Precondition:** a document that was read or a user input contains instructions that lead the agent to request operations or resources outside the approved scope.
- **Mitigation:** the access decision does not use a model. The `AuthorizationEngine` is deterministic, with no I/O, no cache and no clock of its own. The `OperationCatalog` is a closed list with two operations (`sharepoint.document.read` and `sharepoint.document.list`). The arguments must be exactly the expected ones (names and number). There is no URL, path or extra parameter. The capability binds agent, action and canonical resource. The hourly limit (`WalletLimits.MaxActionsPerHour`) brakes volume. The system only narrows what the external authority allows, never widens it.
- **Test:** BT-01 (unauthorised resource fails). TC-01 in part (local): `Unknown_missing_or_extra_arguments_are_rejected`, `Item_identifiers_cannot_carry_paths_or_queries` and `Generic_proxy_batch_and_arbitrary_urls_are_impossible` cover URL, `..` and extra parameters. Embedded instruction text and the proof that `IDocumentGateway` is not called have no dedicated test.
- **Evidence:** `action.denied` event with `reason_code`, and absence of `action.admitted` for the same `correlation_id`.
- **Residual risk:** the product does not inspect the content returned to the agent. An injection that asks only for what the agent can already read within the authorised scope is executed, and the result can be forwarded by the agent (see T-20). The hourly limit reduces volume and does not prevent the first request.
- **Owner:** to be assigned (suggested role: application security lead, with the agent owner).

### T-02 Token theft

- **Boundary:** F1 and F3. Three distinct credentials: the agent's Entra token for the gateway, the executor's data credential, and the pre-authenticated download URL.
- **Precondition:** the attacker obtains the agent's token (runtime memory or logs) or the executor's credential.
- **Mitigation:** the agent never receives the Graph credential, which stays in the `PolicyExecutor`. The agent's token only works for the gateway. APIM validates the token with `validate-azure-ad-token` and the policy model requires `audiences` and the expected client application, because only `tenant-id` is mandatory and a policy with only that would accept any token from the tenant (inference) [M35]. The executor validates the token again, because Microsoft recommends always validating in the backend and warns that "tokens can be replayed" [M38]. There is one execution identity per agent (`ExecutorObjectId`), so a stolen executor credential is limited to the scope of one agent. The internal capability is an opaque reference and not a bearer token. The ledger never stores tokens (BT-18). Suspension blocks admission through the authoritative state, even with a token that is still valid.
- **Test:** BT-02 (forged caller and tenant fail), local. BT-06 (agent without an alternative credential for Graph) and BT-14 (previously issued token tested and real result reported) only in phases C and D. TC-02 to be created (phase C): a valid token from the tenant with another audience or from another client application receives 401 at APIM and is refused at the executor.
- **Evidence:** `action.denied` with `agent_suspended` plus `alert.post_suspension_attempt`; measured result of BT-14 by resource and by identity type.
- **Residual risk:** the documentation does not guarantee invalidating access tokens already issued. Disabling and revoking prevents new tokens, and for applications that use access tokens "the user loses access when the access token expires" [M01]. The default lifetime has sources in disagreement: 60 to 90 minutes, average 75 [M07] [M08], against 1 hour [M01]. Use 90 minutes as the worst case without CAE. With CAE, tokens can last up to 28 hours for users [M02] and up to 24 hours for workload identities [M03]. CAE for workload identities only covers Microsoft Graph, single-tenant service principals of line-of-business applications, and requires the client to declare `cp1` [M03]. Managed identities are not supported by CAE and have a token cache of about 24 hours, with no forced refresh [M03] [M09]. Whether CAE covers agent identities: NOT VERIFIED [M04]. What the gateway denies after suspension remains valid in Entra until it expires, and against Graph directly the stolen token works if the agent has a direct grant (coverage `DirectAccessPossible`).
- **Owner:** to be assigned (suggested role: identity architect, with the Entra administrator).

### T-03 Replay

- **Boundary:** F1, F2 and F5. Three different replays: of the token at the gateway, of the `POST /actions` request, and of containment messages.
- **Precondition:** the attacker, or a network fault, repeats a request or message already seen.
- **Mitigation:** repeated request: the idempotency key is mandatory and its format is validated. The same key and the same arguments return the previous result without repeating the effect and without payload. The same key with different arguments gives `idempotency_conflict`. Repeated token: the authoritative state is read again on every admission, so a token captured before the suspension does not pass after it. Before the suspension, a token replay is indistinguishable from legitimate use, because the token is a bearer token. Messages: see T-14.
- **Test:** BT-10 (message repetition does not duplicate the effect) and BT-03 (suspension invalidates a previous capability still within its validity), local. TC-03 to be created (phase C): a token captured before the suspension and reused afterwards receives `agent_suspended` through the real APIM.
- **Evidence:** `idempotent_replay` and `in_progress` in the responses, `Attempt` in the action record, `alert.post_suspension_attempt`.
- **Residual risk:** with the agent active, a replay within the token validity executes permitted operations. The hourly limit and idempotency reduce the effect. Microsoft warns that gateway configurations can change and tokens can be replayed [M38].
- **Owner:** to be assigned (suggested role: platform engineer responsible for the PolicyExecutor).

### T-04 Confused deputy

- **Boundary:** F2 and F3. The executor is the deputy: it has a data credential that the agent does not have.
- **Precondition:** an agent manages to make the executor use its credential for a resource or agent that does not belong to it.
- **Mitigation:** the caller identity comes from the validated token (`tid` and `oid`), never from `agent_id` in the body (`CallerIdentity`). The agent is resolved by tenant and identity (`FindAgentByIdentityAsync`). The capability is only valid for its tenant, agent, action and canonical resource. The executor uses the agent's own `ExecutorObjectId`, with no universal executor holding whole-tenant rights. In SharePoint, the Selected grant exists on the resource and the identity that assigns permissions is separate from the executor [M22] [M23].
- **Test:** BT-02 and BT-07 (removing one capability does not change others), local. TC-04 to be created (local): agent A requests the resource of an agent B with its own capability and receives `capability_missing`.
- **Evidence:** `action.denied` with `capability_missing` or `agent_unknown`. `executorIdentity` recorded in every `action.admitted`.
- **Residual risk:** security depends on the registry associating identity and `ExecutorObjectId` correctly. A human error in that registry creates a confused deputy. Reconciliation (T-12) detects unexpected grants, not wrong registrations. The `tid` and `oid` claims of an Entra agent token are assumptions: TO BE VALIDATED IN THE TENANT.
- **Owner:** to be assigned (suggested role: security architect).

### T-05 SSRF

- **Boundary:** F2 and F3.
- **Precondition:** the agent can influence where the executor makes HTTP requests (URL, host, path, `@odata.nextLink`, `Location`).
- **Mitigation:** there is no generic HTTP proxy. The caller does not supply a URL: only `siteId`, `driveId` and `itemId`, with a closed format, and the executor builds the Graph request from them. Nothing is normalised: the `ResourceCanonicalizer` rejects what is not canonical. Redirects and pagination links are followed only by the executor and only after being validated against a host list (to be implemented in phase C). The executor runs with no route to internal addresses that it does not need.
- **Test:** TC-05 in part. Local part: arguments that look like a URL are refused (`Generic_proxy_batch_and_arbitrary_urls_are_impossible`). IP address and host have no dedicated test. Phase C part: the executor cannot connect to destinations outside the list (network egress test). BT-05 (gateway and backend not bypassable) only in phase C.
- **Evidence:** `action.denied` with `resource_not_canonical`; executor runtime network egress log in phase C.
- **Residual risk:** the destination hosts of the 302 in SharePoint Online are NOT VERIFIED. The page example uses a personal OneDrive host [M25]. `@odata.nextLink` is an absolute URL in pagination [M27], and must be validated before being followed (inference). If the executor follows links without validation, the threat reopens.
- **Owner:** to be assigned (suggested role: platform engineer, with security review).

### T-06 Batch and redirect bypass

- **Boundary:** F2 and F3.
- **Precondition:** the agent uses a composite request, a query parameter or a redirect to reach operations that the list does not allow.
- **Mitigation:** the `OperationCatalog` has no batch operation. The executor assembles the Graph requests and never forwards the agent's body. APIM denies `/$batch` in `v1.0` and in `beta` by default (policy to be written in phase C). The batch has a limit of 20 inner requests, the outer method is always POST and an outer `200` does not show the result of the inner ones [M29]. This bypasses a list of paths and methods built at the gateway (inference). Parameters such as `$expand` and `$select` are not accepted, because `driveItem` allows `$expand` to bring children [M26] and the metadata carries `@microsoft.graph.downloadUrl` [M28]. Redirect: the executor follows the 302 of `/content` [M25], returns only the bytes, and never returns `Location` nor logs the URL.
- **Test:** TC-06 in part (local): `$batch` and extra arguments are refused (`Generic_proxy_batch_and_arbitrary_urls_are_impossible`) and the response does not contain `downloadUrl` (`BT06_`). `$expand`, `$select` and `Location` have no dedicated test. BT-05 (phase C): a direct call to the executor and a batch through the gateway fail.
- **Evidence:** `action.denied` with `operation_not_allowlisted` or `arguments_invalid`. Executor logs without pre-authenticated URL.
- **Residual risk:** a pre-authenticated URL already issued is not invalidated immediately when the permission is removed ("might not immediately invalidate the URL") [M28]. The validity has two sources in disagreement: "might expire within minutes" [M25] against "1 hour" [M28]. Use 1 hour as the worst case. Whoever holds the URL downloads without a token. The size limit of the batch body is NOT VERIFIED [M29].
- **Owner:** to be assigned (suggested role: platform engineer, with the APIM owner).

### T-07 Manipulated canonical resources

- **Boundary:** F1 and F2.
- **Precondition:** the caller sends the same resource with another spelling (upper case, percent encoding, spaces, trailing slash, Unicode), a path instead of an identifier, or identifiers from different sites combined.
- **Mitigation:** the `ResourceCanonicalizer` only accepts resources that are already canonical and does not normalise. An input that needs trimming, case change or decoding is rejected, because two spellings of the same resource are an authorisation error. The regular expressions are closed and anchored. The comparison between `Capability.Resource` and the resolved canonical resource is ordinal and exact. Addressing is by identifiers only, never by path or URL. The Selected grant on the resource is a second barrier: without an explicit assignment "an application consented ... would initially have no access" [M22].
- **Test:** BT-01 (authorised resource works, unauthorised fails). TC-07 in part (local): `Non_canonical_resources_are_rejected_not_normalised` covers case, spaces, `%2F`, `..`, URL, incomplete resource and line break. Homoglyphs and trailing slash are not in the table.
- **Evidence:** `action.denied` with `resource_not_canonical`. The SHA-256 digest of the arguments makes it possible to prove which arguments were requested without storing them.
- **Residual risk:** the format of the site, drive and item identifiers is an assumption of the design (site: `host,GUID,GUID` in lower case; drive and item: opaque text with `A-Za-z0-9!_-`). The documentation read does not fix it. TO BE VALIDATED IN THE TENANT. If the real format differs, the expected failure is to refuse everything (fail-closed), which blocks use but does not open it.
- **Owner:** to be assigned (suggested role: platform engineer).

### T-08 Cross-tenant escalation

- **Boundary:** F1, F2 and F5.
- **Precondition:** a token or identifier from another tenant is accepted, or a query returns data from another tenant.
- **Mitigation:** the `tid` of the validated token must match the agent's tenant. All `IWalletTx` queries carry `tenantId`: a UUID is not authorisation. The only query that crosses tenants is `ListPendingOutboxAsync`, reserved for the worker. Agent identities are single-tenant and only receive tokens in the tenant where they were created, while blueprints can be multitenant [M14]. The scope of any action on a blueprint must be explicit (see T-10).
- **Test:** BT-02 (forged caller and tenant fail), local. TC-08 (local): `Tenants_are_isolated_for_operators_and_for_callers` and `Operator_of_another_tenant_cannot_see_or_touch_the_agent`. A caller with `(tid, oid)` and no agent gives `agent_unknown`, and an operator from another tenant receives `NotFoundException`, the same as for a non-existent agent.
- **Evidence:** `action.denied` with `agent_unknown`.
- **Residual risk:** the pilot has one tenant. Multitenant isolation is never exercised in a real environment. The cross-tenant outbox query is a deliberate exception that depends on the integrity of the worker. The `tenant-id` configuration in APIM is TO BE VALIDATED IN THE TENANT.
- **Owner:** to be assigned (suggested role: security architect).

### T-09 Compromised operator

- **Boundary:** F4 and F8.
- **Precondition:** an operator or administrator account is controlled by an attacker.
- **Mitigation:** four separate roles (Reader, IncidentOperator, PolicyAdmin, Auditor). Reactivation requires a request from one PolicyAdmin and approval from ANOTHER PolicyAdmin, with external grants reverified, a new `revocation_epoch` and a new `policy_version`. Every operator action records the human initiator. Suspending is fast and requires only IncidentOperator, which is a safe failure: a compromised operator can cause unavailability, not access. Strong authentication and temporary privileged access "when supported and licensed" (section 13 of the specification).
- **Test:** BT-19 (operator without a role cannot suspend, reactivate or grant capabilities), local. TC-09 (local): `Reactivation_needs_second_person_revalidated_grants_and_kills_old_capabilities` and `Activation_needs_a_second_person`.
- **Evidence:** `agent.reactivation_requested` and `agent.reactivated` with distinct human initiators, `agent.suspended` with initiator.
- **Residual risk:** two PolicyAdmins in collusion, or compromised, reactivate. A SQL or Azure administrator outside the application is not stopped by it. The ledger only gives evidence, not prevention: "an attacker or system administrator who has control of the machine can bypass all system checks" [M39]. Temporary privileged access and strong authentication depend on licences to be confirmed.
- **Owner:** to be assigned (suggested role: information security lead).

### T-10 Shared identity

- **Boundary:** F3 and F6.
- **Precondition:** several agents share the same blueprint, the same user-assigned managed identity or the same executor, and the suspension of one affects another or lets another through.
- **Mitigation:** each agent has its own `ExecutorObjectId`. Grants on the agent itself count as a direct path and lower coverage to `DirectAccessPossible`. Disabling by default applies to the individual agent. Disabling a blueprint requires distinct visible scope, impact and approval. The same managed identity is not reused across runtimes with incompatible scopes.
- **Test:** BT-13 (disabling one agent does not disable siblings of the blueprint), only in simulation. BT-06 only in phases C and D. TC-10 NOT implemented: the code only refuses an executor equal to the agent's identity (`Agent_without_owner_or_with_shared_executor_identity_cannot_be_registered`). Two different agents with the same `ExecutorObjectId` are not refused.
- **Evidence:** `coverage.changed`, `alert.direct_access_discovered`, `containment.step` per agent.
- **Residual risk:** the credentials live in the blueprint and are shared by all child identities: "Treat each blueprint as a credential boundary" [M14]. Disabling the blueprint blocks the existing identities and prevents new ones [M06]. The real effect of disabling an agent identity on siblings, and on the associated agent user, is only validated in the tenant in phase D. The local BT-13 only proves that the adapter acts on one object. The cascade to the agent user is NOT VERIFIED [M21].
- **Owner:** to be assigned (suggested role: identity architect).

### T-11 Cumulative grant

- **Boundary:** F3 and F6.
- **Precondition:** the agent has, through another route, a broader permission than intended, and the authorities add up.
- **Mitigation:** the effective access permitted by the product is the intersection of the product's restrictions with the external authority. The `ReconciliationService` compares observed grants (`IExternalGrantSource`) with the recorded ones, and a broad grant (for example `Sites.Read.All` or `Files.Read.All`) is dangerous drift and suspends the agent. In SharePoint, revoking a higher-level scope does not remove the access given by a lower scope ("the application maintains access to the list it was given specific access to") [M22], so reconciliation lists all levels. In Exchange (later phase, outside the MVP), the Entra permissions and those of RBAC for Applications are a union: "the union of an unscoped `Mail.Read` grant from Microsoft Entra and a resource-scoped `Mail.Read` grant in Application RBAC results in no effective resource scoping" [M32]. The ledger must verify both authorities.
- **Test:** BT-15 (grant drift detected and handled), local. TC-11 to be created (phase D, only when mail comes in): a `Mail.Read` permission consented in Entra cancels the RBAC scope, and `Test-ServicePrincipalAuthorization` does not show it because it excludes Entra permissions [M32].
- **Evidence:** `alert.broad_grant`, `alert.drift_detected`, `grant.recorded`, `grant.absent`.
- **Residual risk:** grants given between reconciliation cycles work until they are detected. The interval is not yet defined. Listing and getting permissions on a site through Graph is NOT VERIFIED in the pages read, and reconciliation needs it. In Exchange, a role change takes 30 minutes to 2 hours to propagate, and the test cmdlet ignores that cache [M32].
- **Owner:** to be assigned (suggested role: tenant administrator, with the security architect).

### T-12 Drift

- **Boundary:** F3 and F6.
- **Precondition:** the real state of Entra or of the resource changes outside the product (new consent, new grant, owner removed), and the events are lost or delayed.
- **Mitigation:** mandatory periodic reconciliation, in addition to events. Each `ExternalGrant` stores `LastVerifiedAt`, source and `LastResult`. The agent state and the coverage declare the last verification (`Coverage.LastVerifiedAt`). Dangerous drift suspends the agent or prevents a new activation. Minimum alerts: direct access discovered, drift, broad grant and account without an owner.
- **Test:** BT-15 (grant drift detected and handled), local.
- **Evidence:** `alert.drift_detected`, `alert.broad_grant`, `alert.direct_access_discovered`, `grant.recorded`.
- **Residual risk:** reconciliation needs read permissions in Entra and on the resource, and that matrix is TO BE FILLED IN THE TENANT. An incomplete read hides grants. The interval is an open decision. Entra events are not authority. Agent 365 and the Microsoft 365 admin centre have a second block switch, and whether blocking there changes Entra is NOT VERIFIED [M14] [M64].
- **Owner:** to be assigned (suggested role: platform engineer, with the Entra administrator).

### T-13 Concurrent revocation

- **Boundary:** F2, F5 and F3.
- **Precondition:** a suspension arrives while an action has already been admitted or is on its way to Graph.
- **Mitigation:** admission reads the agent through the locking path (`GetAgentAsync` holds the record until the end of the transaction), so admission and suspension are serialised. Transaction 2 revalidates state, epoch and capability and only then marks `Executing`. Suspension is a single transaction: it changes the state, increments `revocation_epoch`, creates the `ContainmentOperation` in `LocalBlocked`, records `InFlightActionIds`, and writes outbox and audit. `Agent.Version` gives optimistic concurrency control. Actions already in `Executing` cannot be stopped and are reported as in progress, separately.
- **Test:** BT-04 (suspension and execution race documents exactly the in-flight actions) and BT-03 (suspension invalidates a previous capability that is still valid), local.
- **Evidence:** `InFlightActionIds` in the containment operation, `action.cancelled_at_revalidation`, `action.executing`, `agent.suspended`.
- **Residual risk:** revalidation does not eliminate the race with a request already sent to Graph. There is no universal rollback. In the MVP there is only reading, and the bytes may already have been delivered when the suspension is persisted. The 5-second SLO for refusing new admissions is an initial objective, to be measured in phase C.
- **Owner:** to be assigned (suggested role: platform engineer responsible for the core).

### T-14 Out-of-order messages

- **Boundary:** F5 and F6.
- **Precondition:** an outbox message arrives duplicated, late or after a reactivation, for example an old containment order.
- **Mitigation:** each message carries the `Epoch`. An epoch different from the current one means discarded as `superseded` (`containment.superseded`), never executed. The worker never reactivates an agent. Consumers revalidate before executing and after retries. Old messages do not reactivate wallets. Order is not guaranteed by the transport. FIFO requires sessions in Service Bus [M56], and the availability of sessions per tier is NOT VERIFIED.
- **Test:** TC-14 (local): `Old_containment_message_never_acts_on_a_reactivated_agent_and_never_reactivates`: a message from a previous epoch, delivered after the reactivation, is discarded and audited, and does not change the state. BT-03 and BT-10 are related.
- **Evidence:** `containment.superseded`, `containment.dead_lettered`.
- **Residual risk:** adapter steps of the same epoch can arrive out of order, and depend on being idempotent. The dead-letter queue does not clean itself ("there's no automatic cleanup of the DLQ") [M52], so it needs an operational process.
- **Owner:** to be assigned (suggested role: platform engineer responsible for the ContainmentWorker).

### T-15 Duplicate retry

- **Boundary:** F1, F5 and F6.
- **Precondition:** the client repeats `POST /actions`, Service Bus redelivers a message, or the worker repeats a step after throttling.
- **Mitigation:** mandatory idempotency key, per agent. The same key and arguments: previous result, without repeating the effect. Different arguments: `idempotency_conflict`. Action still running: `in_progress`. Containment adapters are idempotent, with timeout, bounded retries and backoff, and respect `Retry-After` [M30] [M31]. Service Bus in peek-lock mode gives at-least-once delivery, and "if your scenario can't tolerate duplicate processing, add extra logic" [M50], so the consumer is idempotent. The ledger does not allow correcting a duplicated row (inference from [M42]).
- **Test:** BT-10 (message repetition does not duplicate the effect), local.
- **Evidence:** `idempotent_replay`, `in_progress`, `Attempt`, `containment.step` with attempts.
- **Residual risk:** there is no universal exactly-once for Graph effects. Reading is idempotent by nature. The phases with writing (mail drafts) require intent recording, reconciliation and, when necessary, human confirmation. Service Bus duplicate detection only acts on send, within the window (10 minutes by default) [M51], and does not cover redelivery to the consumer.
- **Owner:** to be assigned (suggested role: platform engineer).

### T-16 SQL unavailability

- **Boundary:** F5.
- **Precondition:** SQL does not respond, or cannot persist the intent or the result.
- **Mitigation:** fail-closed. A `StoreUnavailableException` at admission gives `denied` with `state_unavailable`. There is no positive cache. The intent must be persisted before any external effect. If revalidation cannot read the state, the action stays `Admitted` with no effect and the sweep (`SweepStaleActionsAsync`) closes it. If the result cannot be written, the executor does not deliver the data (`result_not_recorded`) and the action is left for the sweep, which marks it `Unknown`. The unavailability of secondary telemetry must not cause silent loss of the primary record.
- **Test:** BT-08 (SQL unavailable: deny new protected actions), local. BT-17 (restore and failover do not reactivate a suspended agent) only in phases C and D.
- **Evidence:** `state_unavailable` response from the executor, recorded in operational telemetry (secondary: during the unavailability there is no ledger record). Availability alert.
- **Residual risk:** the availability of SQL becomes the availability of the product, by choice. A restore can bring back an earlier state in which the agent was active, so a restore requires validating RPO and reconciling before reopening traffic (BT-17). Digests only cover data replicated to geo-secondaries, by Microsoft's design [M41]. An action whose effect occurred and whose result was not recorded stays `Unknown`.
- **Owner:** to be assigned (suggested role: DBA or data platform engineer).

### T-17 APIM unavailability

- **Boundary:** F1 and F2.
- **Precondition:** APIM is down or misconfigured.
- **Mitigation:** APIM is the entry point, not the authority. The executor validates the token and reads the authoritative state on its own [M38]. With APIM down the agent does not reach the executor, because the executor has no direct exposure (internal ingress, to be defined in the phase C Bicep). There is no contingency path that bypasses the gateway.
- **Test:** TC-17 to be created (phase C): taking APIM down makes requests fail, and a direct call to the executor without the APIM credential is refused. BT-05 (phase C): gateway and backend cannot be bypassed.
- **Evidence:** gateway availability alerts, failed requests in APIM, refusals at the executor.
- **Residual risk:** the network mechanisms apply to the APIM to executor leg [M55], not to Graph (inference). APIM availability per tier is NOT VERIFIED. Whoever edits APIM policies can authenticate as the service's managed identity and extract the token, and APIM "does not validate which backend the token is sent to" [M37]. For that reason the Graph credential stays in the executor and not in APIM (inference).
- **Owner:** to be assigned (suggested role: integration platform lead, APIM).

### T-18 Entra unavailability

- **Boundary:** F1, F4 and F6.
- **Precondition:** Entra does not issue tokens, does not validate, or does not accept the calls of the containment adapters.
- **Mitigation:** the local block (`LocalBlocked`) does not depend on Entra and remains effective. The adapters have timeout, bounded retries, backoff and dead-letter. The result is visible per step (`Failed` or `Unknown`) and the global state does not become `VerifiedScoped`: any `Unknown` gives `Unknown`, a mix gives `Partial`, all failed gives `Failed`. Signature validation uses a key cache: in the `validate-jwt` policy the keys are fetched every hour and stored, with a read at most every 5 minutes [M36]. For `validate-azure-ad-token` that behaviour is NOT VERIFIED. Operator access during an Entra failure depends on break-glass that is independent of the application.
- **Test:** TC-18 (local, with failures injected in the simulation): `Repeated_failure_is_bounded_and_reported_as_failed` and `Suspended_never_becomes_verified_without_external_steps`. Timeout, throttling and error in the adapters produce `Unknown`, `Partial` or `Failed`, never `VerifiedScoped`. BT-11 and BT-12 are related. BT-20 (break-glass with the application unavailable) only in phase D.
- **Evidence:** `containment.step`, `containment.completed` with the global state, `containment.dead_lettered`, uncertainties field of the `ContainmentOperation`.
- **Residual risk:** without Entra, external containment does not advance. The local block only covers the path through the gateway. An agent with a direct grant keeps access. Tokens already issued remain (see T-02).
- **Owner:** to be assigned (suggested role: identity architect, with the continuity lead).

### T-19 Digest failure

- **Boundary:** F7.
- **Precondition:** digests stop being exported, go to a destination that can be tampered with, or the verification does not run.
- **Mitigation:** native SQL Ledger digest management to independent storage. Digests are generated at an interval of 30 seconds, and only if there are transactions [M41]. Baseline: Blob with a validated immutability policy and `locked` once the retention is approved [M46]. SQL privileges, digest destination and verification kept separate. Monitoring of the age of the last digest, export failures, verification result and time coverage. Microsoft recommends alerts on failed uploads and on a drop in successful uploads [M45]. Verification after restore and failover.
- **Test:** BT-16 (evidence tampering detected in the scenarios supported by the ledger). TC-19 to be created (phase C): revoking the write permission on the storage triggers the alert, and pointing the upload at a destination without a `locked` policy is detected by the configuration check.
- **Evidence:** digest alerts, verification return code (`0` success, `1` failure) and `last_verified_block_id` [M40].
- **Residual risk:** the ledger gives evidence of tampering, not prevention, and does not prevent a false initial entry, nor guarantee availability, nor recover data [M39]. "The verification process and the integrity of the database depend on the integrity of the input digests" [M40]. With Entra-only authentication, Elastic Jobs is not supported, so the verification scheduling uses Azure Automation or a dedicated job [M40] [M53]. LRS is not supported for automatic digests [M42]. The names of the upload metrics are NOT VERIFIED. Azure Confidential Ledger as a destination has a stronger integrity guarantee against privileged administrators [M41], with its own limits [M49], and is an option still to be decided.
- **Owner:** to be assigned (suggested role: DBA with the security lead, and the auditor for the verification).

### T-20 Exfiltration

- **Boundary:** F1, F3 and F7.
- **Precondition:** data accessible to the agent leaves through an unforeseen path: the agent itself, a pre-authenticated URL, logs and ledger, or direct access to the resource.
- **Mitigation:** least privilege per resource (Selected, `read` role) and two read operations. Hourly limit. The ledger stores a SHA-256 digest of the arguments and never tokens, content, prompts or unnecessary personal data. The executor returns bytes and not the `Location`. In the strong control profile the agent's direct accesses to the resource are removed, and restricted egress is an additional defence, not a substitute. Defender, Sentinel and Purview are to be assessed and coverage is not presumed.
- **Test:** BT-18 (no secrets or unnecessary sensitive content in the ledger and the logs), local. BT-06 only in phases C and D. TC-20 to be created (phase C): the agent runtime has no route to Graph or to the storage.
- **Evidence:** `action.completed` with digest and minimal reference, absence of content in the records, `alert.direct_access_discovered`.
- **Residual risk:** what the agent can read can be sent out by it. Deleting data that has already been exfiltrated is outside the MVP. The pre-authenticated URL is a bearer credential, requiring no token, with a documented validity between "minutes" [M25] and "1 hour" [M28], and removing permissions may not invalidate it immediately [M28]. Defender and Purview coverage is TO BE VALIDATED IN THE TENANT.
- **Owner:** to be assigned (suggested role: data protection lead, with the security lead).

### T-21 External operation with unknown result

- **Boundary:** F3 and F6.
- **Precondition:** the request to Graph, or a containment step, ends without confirmation (timeout, network error, uninterpretable response).
- **Mitigation:** the external call has a timeout (`ExecutionOptions.ExternalTimeout`, 10 seconds by default in the code, to be calibrated in phase C). A timeout gives `ActionState.Unknown` with `external_timeout`, and an unclassified error gives `Unknown` with `external_error_unknown`, never an invented success. Throttling gives `Failed` with `external_throttled`, and the executor respects `Retry-After`: Graph returns 429 [M30] and SharePoint 429 or 503 [M31], and throttled requests inside a batch are not repeated automatically [M30]. In containment, any `Unknown` step makes the global state `Unknown`. The sweep closes actions without a result as `Unknown`. Reconciliation and human confirmation handle what remained unknown.
- **Test:** BT-11 (external timeout produces unknown and reconciliation), BT-12 (throttling generates a visible partial state) and BT-04, local, over failures injected in the simulated world.
- **Evidence:** `action.completed` with `execution_state` `Unknown`, `containment.step`, list of uncertainties of the containment operation.
- **Residual risk:** it is never known whether a read that timed out was served. For write effects (later phases) it is necessary to reconcile with the source. There is no universal exactly-once, nor universal rollback. The real SharePoint limits depend on the number of licences, and load tests are not permitted in SharePoint Online [M31], which limits phase E.
- **Owner:** to be assigned (suggested role: platform engineer, with the incident operator).

## 4. Summary: threat, test and status

Status criterion, applied to the main mitigation of each threat:

- **covered locally:** the main mitigation has a BT or TC with local proof, over the simulated world. Part may remain unproven or unimplemented, stated on the row.
- **phases C/D only:** the main mitigation is only proven with real resources, in phases C or D.
- **to be created:** the specification has no BT for the main mitigation and the `TC` test can only be done in phase C.

A "covered locally" classification is not proof of real containment: the Microsoft world is simulated and a 200 response or a simulation does not prove containment (section 18 of the specification).

| Threat | BT | Tests to create | Status |
|---|---|---|---|
| T-01 Prompt injection | BT-01 | TC-01 (local) | covered locally |
| T-02 Token theft | BT-02 (local), BT-06 and BT-14 (C/D) | TC-02 (phase C) | phases C/D only |
| T-03 Replay | BT-10, BT-03 | TC-03 (phase C) | covered locally |
| T-04 Confused deputy | BT-02, BT-07 | TC-04 (local) | covered locally |
| T-05 SSRF | BT-05 (C/D, complementary) | TC-05 (local and phase C) | covered locally (partial: egress connection only in phase C) |
| T-06 Batch and redirect bypass | BT-05 (C/D, complementary) | TC-06 (local) | covered locally (partial: `$expand`, `$select` and `Location` without a dedicated test) |
| T-07 Manipulated canonical resources | BT-01 (complementary) | TC-07 (local) | covered locally |
| T-08 Cross-tenant escalation | BT-02 | TC-08 (local) | covered locally |
| T-09 Compromised operator | BT-19 | TC-09 (local) | covered locally |
| T-10 Shared identity | BT-13 (simulation only), BT-06 (simulation only) | TC-10 (not implemented) | covered locally (partial: BT-13 and BT-06 only in simulation, real effect in phase D; TC-10 to be implemented) |
| T-11 Cumulative grant | BT-15 | TC-11 (phase D, mail) | covered locally (SharePoint; Exchange outside the MVP) |
| T-12 Drift | BT-15 | none | covered locally |
| T-13 Concurrent revocation | BT-04, BT-03 | none | covered locally |
| T-14 Out-of-order messages | BT-03, BT-10 (related) | TC-14 (local) | covered locally |
| T-15 Duplicate retry | BT-10 | none | covered locally |
| T-16 SQL unavailability | BT-08 (local), BT-17 (C/D) | none | covered locally (restore only in phases C/D) |
| T-17 APIM unavailability | BT-05 (C/D) | TC-17 (phase C) | to be created |
| T-18 Entra unavailability | BT-11, BT-12 (related), BT-20 (C/D) | TC-18 (local) | covered locally (partial: BT-20 only in phase D) |
| T-19 Digest failure | BT-16 (complementary) | TC-19 (phase C) | to be created |
| T-20 Exfiltration | BT-18 (local), BT-06 (C/D) | TC-20 (phase C) | covered locally (partial: direct paths only in phases C/D) |
| T-21 Unknown result | BT-11, BT-12, BT-04 | none | covered locally |

Count: 18 covered locally, 1 phases C/D only, 2 to be created, out of 21 threats in total. Among the 18, T-05, T-06, T-10, T-11, T-16, T-18 and T-20 have a part still to be proven or implemented, stated on the row.

## 5. Blocking tests of the specification (section 15)

| Id | Test | Proof |
|---|---|---|
| BT-01 | Authorised resource works, unauthorised fails | local |
| BT-02 | Forged caller and tenant fail | local |
| BT-03 | Suspension invalidates a previous capability still within its validity | local |
| BT-04 | Suspension and execution race documents exactly the in-flight actions | local |
| BT-05 | Gateway and backend cannot be bypassed | phases C/D only |
| BT-06 | The agent has no alternative credential for Graph | simulation only (real: phases C/D) |
| BT-07 | Removing one capability does not improperly change others | local |
| BT-08 | SQL unavailable: deny new protected actions | local, real SQL |
| BT-09 | Bus unavailable: the persistent block remains effective | local |
| BT-10 | Message repetition does not improperly duplicate the effect | local, real SQL |
| BT-11 | External timeout produces unknown and reconciliation, not an invented success | local |
| BT-12 | Microsoft throttling generates a visible partial state | local |
| BT-13 | Disabling one agent does not disable siblings of the blueprint | simulation only (real: phase D) |
| BT-14 | Test a previously issued token on a test resource and report the real result | simulation only (real: phases C/D) |
| BT-15 | Grant drift is detected and handled | local |
| BT-16 | Evidence tampering is detected in the scenarios supported by the ledger | local, real SQL |
| BT-17 | Restore and failover do not reactivate a suspended agent, with RPO and reconciliation validated | phases C/D only |
| BT-18 | No secrets or unnecessary sensitive content in the ledger and in the logs | local |
| BT-19 | Operator without a role cannot suspend, reactivate or grant capabilities | local |
| BT-20 | Break-glass works with the application unavailable | phases C/D only |

Local BT-16 runs against a SQL Server 2022 in a container (`AP_TEST_SQL`, see `test-plan.md`). Verification against digests stored in independent immutable storage only exists in phase C.

## 6. Tests to be created (TC)

| Id | Threat | Description | Where it runs |
|---|---|---|---|
| TC-01 | T-01 | Arguments with URL, `..`, extra parameters or instruction text are refused without calling the data gateway | local, unit |
| TC-02 | T-02 | Tenant token with the wrong audience or from another client application: 401 at APIM and refusal at the executor | phase C |
| TC-03 | T-03 | Token captured before the suspension and reused afterwards receives `agent_suspended` through APIM | phase C |
| TC-04 | T-04 | Agent A requests a resource of agent B with its capability and receives `capability_missing` | local |
| TC-05 | T-05 | URL-like, IP, scheme or host arguments refused. In phase C, the executor does not connect to destinations outside the list | local and phase C |
| TC-06 | T-06 | `$batch`, `$expand`, `$select` refused. The response never contains `Location` or `downloadUrl` | local, unit |
| TC-07 | T-07 | Table of spelling variants of the resource, all with `resource_not_canonical` | local, unit |
| TC-08 | T-08 | `agentId` from another tenant returns a response identical to that of a non-existent agent | local |
| TC-09 | T-09 | The same PolicyAdmin does not request and approve the reactivation | local |
| TC-10 | T-10 | Two agents with the same `ExecutorObjectId` are refused | local |
| TC-11 | T-11 | `Mail.Read` permission in Entra cancels the RBAC for Applications scope | phase D, when mail comes in |
| TC-14 | T-14 | Message from a previous epoch delivered after the reactivation is discarded and audited | local |
| TC-17 | T-17 | APIM unavailable makes requests fail. Direct call to the executor refused | phase C |
| TC-18 | T-18 | Failures injected in the adapters never produce `VerifiedScoped` | local |
| TC-19 | T-19 | Revoking write on the storage triggers the digest alert. Destination without a `locked` policy is detected | phase C |
| TC-20 | T-20 | The agent runtime has no route to Graph or to the storage | phase C |

## 7. Gaps in this document

- Nothing was validated in a tenant. All claims, identifier formats and revocation behaviours marked "TO BE VALIDATED IN THE TENANT" remain to be validated.
- The status of the local BT tests follows the design. Confirm against the suite when it is stable.
- The list of gaps in the Microsoft documentation, with impact, is in `gaps-and-assumptions.md`. The risk register is in `risks.md`.

## 8. Sources

All verified on 2026-11-11. Prefix: `https://learn.microsoft.com/en-us/`. The "Reading" column indicates whether the page was read in full or only from a search excerpt. What comes from an excerpt cannot, on its own, support a decision.

| Key | Path | Reading |
|---|---|---|
| M01 | `entra/identity/users/users-revoke-access` | full |
| M02 | `entra/identity/conditional-access/concept-continuous-access-evaluation` | full |
| M03 | `entra/identity/conditional-access/concept-continuous-access-evaluation-workload` | full |
| M04 | `entra/identity/conditional-access/agent-id` | full |
| M06 | `entra/agent-id/manage-agent-identities-admin` | full |
| M07 | `entra/identity-platform/configurable-token-lifetimes` | full |
| M08 | `entra/identity-platform/access-tokens` | full |
| M09 | `entra/identity/managed-identities-azure-resources/managed-identities-faq` | full |
| M14 | `microsoft-agent-365/developer/identity` | full |
| M21 | `entra/agent-id/agent-users` | full |
| M22 | `graph/permissions-selected-overview` | full |
| M23 | `graph/api/site-post-permissions?view=graph-rest-1.0` | full |
| M25 | `graph/api/driveitem-get-content?view=graph-rest-1.0` | full |
| M26 | `graph/api/driveitem-get?view=graph-rest-1.0` | full |
| M27 | `graph/api/driveitem-list-children?view=graph-rest-1.0` | full |
| M28 | `graph/api/resources/driveitem?view=graph-rest-1.0#instance-attributes` | excerpt |
| M29 | `graph/json-batching` | full |
| M30 | `graph/throttling` | full |
| M31 | `sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online` | full |
| M32 | `exchange/permissions-exo/application-rbac` | full |
| M35 | `azure/api-management/validate-azure-ad-token-policy` | full |
| M36 | `azure/api-management/validate-jwt-policy` | excerpt |
| M37 | `azure/api-management/api-management-howto-use-managed-service-identity#security-considerations-for-managed-identities` | excerpt |
| M38 | `entra/msidweb/advanced/api-gateways#follow-best-practices` | excerpt |
| M39 | `sql/relational-databases/security/ledger/ledger-overview` | full |
| M40 | `sql/relational-databases/security/ledger/ledger-database-verification` | full |
| M41 | `sql/relational-databases/security/ledger/ledger-digest-management` | full |
| M42 | `sql/relational-databases/security/ledger/ledger-limits` | full |
| M45 | `sql/relational-databases/security/ledger/ledger-monitor-digest-uploads` | full |
| M46 | `azure/storage/blobs/immutable-storage-overview` | full |
| M49 | `azure/confidential-ledger/limits` | full |
| M50 | `azure/service-bus-messaging/service-bus-message-loss-and-duplicates` | excerpt |
| M51 | `azure/service-bus-messaging/duplicate-detection` | full |
| M52 | `azure/service-bus-messaging/service-bus-dead-letter-queues` | full |
| M53 | `azure/azure-sql/database/authentication-azure-ad-only-authentication` | full |
| M55 | `azure/api-management/secure-api-management#network-security` | excerpt |
| M56 | `azure/service-bus-messaging/message-sessions` | excerpt |
| M64 | `microsoft-agent-365/admin/manage-agents` | excerpt |
