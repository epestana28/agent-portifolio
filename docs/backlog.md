# Backlog

Project: Agent Portifolio. Phase A document. Verification date of the Microsoft sources: 2026-11-11.

Order: by risk, the highest first, within each phase. The phases follow section 16 of the specification, A to E. The risks referred to (`R-nn`) are in `risks.md`, the threats (`T-nn`) and tests (`BT-nn`, `TC-nn`) in `threat-model.md`, the gaps (`L-nn`) and assumptions (`P-nn`) in `gaps-and-assumptions.md`.

## 1. Real status

No phase advances without the gate of the previous one approved by a person. The status below has not been inflated.

| Phase | Status | Gate (section 16 of the specification) | Gate status |
| --- | --- | --- | --- |
| A. Validation | Documents written from official documentation, without a tenant. Fields that depend on the tenant stay "TO BE FILLED IN THE TENANT". | Approval of scope, licences and permissions. | To be approved by a human. |
| B. Local slice | Implemented in .NET 8, without real credentials. The Microsoft world is simulated and marked as simulation. | Invariants and concurrency tested. | To be approved by a human. Local suite to be confirmed (see B-01). |
| C. Test Azure | To do. Bicep written, never applied. | Proof that direct access is impossible in the strong profile. | Not applicable. |
| D. Containment | To do. No real adapter for Entra or Graph. | Verified coverage, visible uncertainties and independent runbook. | Not applicable. |
| E. Pilot | To do. | Human approval for production. Zero critical risks that are unmitigated or not formally accepted. | Not applicable. |

Phase A documents foreseen in the specification: `environment-assessment.md`, `architecture.md`, `threat-model.md`, `permission-matrix.md`, `coverage-matrix.md`, `cost-estimate.md`, ADRs and risks. This set contains the threat model, the risks, the gaps and assumptions, this backlog and the nine ADRs.

## 2. What already exists at Microsoft and the minimum to build

Derived from section 3 of the specification (Microsoft-first rule), with the verified state of the documentation. "Own code admitted" is the limit: what is outside this column is not built.

Each Microsoft fact carries a key `Mxx` that points to the sources table at the end (Microsoft Learn URL and date 2026-11-11).

| Need | Already exists at Microsoft (verified state) | Minimum to build | Where in the repository |
| --- | --- | --- | --- |
| Agent identity | Entra Agent ID, "available for all Microsoft Entra customers", Graph APIs in `v1.0`. Textual GA NOT VERIFIED [M60] [M18]. An agent identity is a service principal and the credentials live in the blueprint [M14]. | Adapter per identity type (`IdentityType`) and correlation with the registered agent. | `src/Core` (model), adapters in `src/Simulation` (today simulated only) |
| Existing workloads | Managed identities and service principals. Managed identity: no documented disable, cache of about 24 hours, no forced refresh [M09]. | Separate adapters per type. Runtime isolation and removal of grants. | `src/ContainmentWorker`, `src/Simulation` |
| Inventory and owners | Agent 365 in GA since 2026-05-01, Commercial segment, per-user licence [M16]. Creating the Entra identity does not register the instance in Agent 365 [M15]. | Aggregated view and coverage fields. Do not duplicate the native inventory. | `src/ControlApi` |
| Governed entry | Azure API Management with `validate-azure-ad-token` [M35]. The AI gateway governs model and tool traffic, not Graph semantics [M34]. | APIM policy template (audience, client application, deny `$batch`). Validation of capabilities and resources in the executor. | `infra/bicep`, `src/PolicyExecutor` |
| SharePoint permissions | Selected permissions: `Sites.Selected`, `Lists.SelectedOperations.Selected`, `ListItems.SelectedOperations.Selected`, `Files.SelectedOperations.Selected`. Consent without assignment on the resource gives no access [M22]. GA per level NOT VERIFIED. | Orchestration of granting and verification. Assigning identity separate from the executor. | `src/ContainmentWorker`, `src/ControlApi` (to be done in phase C) |
| Mail (later phase) | Exchange RBAC for Applications. Adds to the Entra permissions. There is no drafts-only role [M32]. GA NOT VERIFIED. | Executor restricted to permitted operations. Negative tests. Outside the MVP. | Not started |
| Verifiable history | Azure SQL Database Ledger, append-only tables [M44], automatic digests [M41], verification [M40]. Evidence of tampering, not prevention [M39]. | Schema `ap.AuditEvent` and domain events. | `src/Storage.SqlServer` |
| Independent digests | Immutable Blob with a `locked` policy [M46]. Azure Confidential Ledger as an option, with limits [M49]. | Scheduling and complementary verification. Alerts. | `infra/bicep`, `runbooks` |
| Secrets | Key Vault. Secretless identities where possible. | No vault of our own. | `infra/bicep` |
| Observability | Agent 365 with OpenTelemetry. The Agent 365 observability SDK is deprecated, the Microsoft OpenTelemetry Distro is recommended [M15]. Azure Monitor and Application Insights. | Instrumentation and correlation of the control service and the executor. Does not replace the ledger. | `src/*` |
| Detection and data | Observability data of registered agents appears in Microsoft Defender (activity views and `CloudAppEvents`), in the Microsoft 365 admin centre and in Purview [M15]. Sentinel was not researched. Effective coverage NOT VERIFIED. | Integration, not replacement. To be assessed in phase D or E. | Not started |
| Infrastructure | Bicep. Azure Policy was not researched: NOT VERIFIED. | Modules of the product. | `infra/bicep` |
| Fine-grained authorisation of operations | No documented capability for Graph, SharePoint or Exchange semantics in APIM [M34]. | Everything: `AuthorizationEngine`, `OperationCatalog`, `ResourceCanonicalizer`, `ExecutionService`. | `src/Core` |
| Wallet, capabilities, epoch | No equivalent exists. | Everything: model, `IWalletStore.TransactAsync`, suspension, outbox. | `src/Core`, `src/Storage.SqlServer` |
| Containment states and per-step report | No equivalent exists. | Everything: `ContainmentOperation`, separate states, probes. | `src/Core`, `src/ContainmentWorker` |

Summary of the minimum: the product builds the layer of wallet, authorisation, containment and evidence. It does not build identity, ledger, gateway, vault or token issuer.

## 3. Phase A: validation

Gate: approval of scope, licences and permissions. Status: to be approved by a human.

Items by risk, the highest first. Tasks of decision or of filling in only.

| Id | Item | Risk | Depends on | Output |
| --- | --- | --- | --- | --- |
| A-01 | Decide the retention, pseudonymisation and deletion of data in the ledger and in the digests, and `isLedgerOn`, before any lock or creation of the database. | R-05 | Data protection officer | Decision recorded |
| A-02 | Approve the handling of the Selected contradiction: assume `Sites.Selected` with `read` and require endpoint-by-endpoint confirmation in phase C. Forbid `Files.Read.All` and `Sites.Read.All`. | R-07 | Project owner | Decision recorded |
| A-03 | Confirm licences (Agent 365, Entra P1 or P2, E7) and incremental costs. | R-04, R-15 | Project owner, commercial | Licences approved |
| A-04 | Accept or refuse each preview and beta dependency, with the alternative for each (ADR 0009). | R-01 | Project owner | ADR 0009 approved |
| A-05 | Decide the destination of the digests: immutable Blob (baseline) or Confidential Ledger. | R-14 | Security | ADR 0004 approved |
| A-06 | Assign owners of the threats and risks. | R-25 | Project owner | Table with names |
| A-07 | Approve the pilot scope: one tenant, one runtime, one read agent, one library, negative test on another resource. | R-17 | Project owner | Scope approved |
| A-08 | Decide whether Agent 365 is in scope and whether the agent user stays out. | R-11 | Project owner | Decision recorded |
| A-09 | Define the interval of periodic reconciliation and the target RPO and RTO. | R-21 | Project owner, platform | Values approved |
| A-10 | Fill in the "TO BE FILLED IN THE TENANT" fields of `environment-assessment.md`. | R-17 | Tenant access | Document complete. Blocked without a tenant. |
| A-11 | Re-read in full the pages verified only as excerpts before citing them in a decision (L-42). | R-01 | None | Complete sources |
| A-12 | Approve the phase A gate. | all | A-01 to A-09 | Gate approved by a human |

## 4. Phase B: local slice

Gate: invariants and concurrency tested. Status: implemented, gate to be approved by a human.

Implemented: core (`src/Core`), SQL Server storage (`src/Storage.SqlServer`), simulated world with fault injection (`src/Simulation`), control API, executor, containment worker, Docker image and docker compose with SQL Server 2022. None of this uses real credentials.

Items by risk, the highest first. The remaining work is to close the tests and confirm the status.

| Id | Item | Risk | Output |
| --- | --- | --- | --- |
| B-01 | DONE on 2026-11-11, see `test-plan.md`. Confirmed against the suite: 78 tests, 78 passed with `AP_TEST_SQL`, 73 passed and 5 ignored without it. Each BT test has the prefix `BTnn_` in the method. BT-06, BT-13 and BT-14 exist only in simulation. BT-05, BT-17 and BT-20 have no local proof (BT-20 has no test). | R-16 | BT table with confirmed status |
| B-02 | Concurrency tests: suspension against admission and execution (BT-03, BT-04), with documented in-flight actions. DONE in memory (`BT04_`). The race of suspension against real SQL is missing: only the action limit and idempotency have a concurrent test on real SQL. | R-02 | Concurrency gate |
| B-03 | TC-06 (batch, `$expand`, `$select`, `Location` and `downloadUrl` never returned) and TC-05 (URL-type arguments). PARTLY DONE: `Generic_proxy_batch_and_arbitrary_urls_are_impossible` and `BT06_`. There is no dedicated test for `$expand`, `$select` or `Location`. | R-09 | Unit tests |
| B-04 | TC-07: spelling variants of the resource, all refused. DONE: `Non_canonical_resources_are_rejected_not_normalised`. | R-17 | Unit test |
| B-05 | TC-14: message of a previous epoch discarded as `superseded` after reactivation. DONE: `Old_containment_message_never_acts_on_a_reactivated_agent_and_never_reactivates`. | R-18 | Local test |
| B-06 | TC-09 and TC-10: reactivation by two distinct PolicyAdmins, and a single execution identity per agent. TC-09 DONE. TC-10 TO DO: the code only refuses an executor equal to the agent identity, not two agents with the same `ExecutorObjectId`. | R-20, R-12 | Local tests |
| B-07 | TC-18: faults injected into the adapters never produce `VerifiedScoped`. DONE: `Repeated_failure_is_bounded_and_reported_as_failed` and `Suspended_never_becomes_verified_without_external_steps`. | R-16 | Local test with injected faults |
| B-08 | TC-01, TC-04, TC-08: hostile arguments, crossed capability, tenant isolation. PARTLY DONE: `Unknown_missing_or_extra_arguments_are_rejected`, `Capability_of_another_agent_or_tenant_never_matches`, `Operator_of_another_tenant_cannot_see_or_touch_the_agent`. Still to confirm that the response for an agent of another tenant is the same as for a non-existent agent. | R-12 | Local tests |
| B-09 | Mark the simulation in each simulated adapter and in the documentation. DONE in the code (`X-AgentPortifolio-Simulation` header, warning at start-up, `SIMULATED` prefix in the BT-14 test). Review the texts. | R-16 | Review |
| B-10 | Approve the phase B gate. | R-16 | Gate approved by a human |
| B-11 | Decide `Storage:EnsureSchema`: by default it is `true` and creating the ledger table requires `ENABLE LEDGER`, which the design does not give to any execution identity. The Bicep sets `Storage__EnsureSchema=false` and the schema is applied with an administrative account. | R-05 | Decision and, if applicable, default `false` in the code |
| B-12 | Wire up what exists and no host calls: `ExecutionService.SweepStaleActionsAsync`, periodic reconciliation, and containment when retiring an agent (`retire` only raises the epoch). | R-18 | Scheduling and tests |

## 5. Phase C: test Azure

Gate: proof that direct access is impossible in the strong profile. Status: to do. Blocked by: phase A gate approved, subscription and tenant (see `gaps-and-assumptions.md`, section 4.3).

Items by risk, the highest first.

| Id | Item | Risk | Tests | Blocked by |
| --- | --- | --- | --- | --- |
| C-01 | Prove that direct access is impossible: agent without a Graph credential, without a network route, backend that cannot be bypassed. | R-12, R-06 | BT-05, BT-06, TC-17, TC-20 | Subscription, Bicep |
| C-02 | Validate the permission matrix endpoint by endpoint for the four operations, with an authorised resource and a negative resource. Decide the contradictions L-19, L-20 and L-21. | R-07 | BT-01 | Tenant |
| C-03 | Validate `tid` and `oid` of a real agent identity token, and the formats of site, drive and item. | R-17 | BT-02, TC-07 | Tenant, test agent identity |
| C-04 | APIM policy template: `validate-azure-ad-token` with audience and client application, deny `$batch`. The executor validates the token. | R-06, R-09 | TC-02, TC-03, TC-06 | APIM tier |
| C-05 | Observe the hosts of the 302 in SharePoint Online and fix the egress list of the executor. | R-08 | TC-05 | Tenant |
| C-06 | SQL Ledger: append-only table, automatic digests to Blob with a `locked` policy, after retention is approved. Lock as a manual step. | R-05, R-14 | BT-16, TC-19 | A-01, subscription |
| C-07 | Digest alerts: age, export failure, drop in successful uploads, verification result. | R-14 | TC-19 | C-06 |
| C-08 | Scheduled ledger verification with Azure Automation or a dedicated job (Entra-only). | R-19 | BT-16 | Decision A-05 |
| C-09 | Outbox to Service Bus: duplicate detection, `maxDeliveryCount`, DLQ handling, idempotent consumers. | R-18 | BT-09, BT-10, TC-14 | Subscription |
| C-10 | Restore and failover: the suspended agent stays suspended, RPO validated, reconciliation before reopening traffic. | R-21 | BT-17 | Azure SQL with geo-replication |
| C-11 | Measure the 5-second SLO for refusing new admissions after persisted suspension, with p50, p95 and p99. | R-23 | BT-03 | Real APIM and executor |
| C-12 | Compile the Bicep, `what-if` and apply with approval. Record the accepted `apiVersion` values (L-39). | R-15, R-17 | none | Subscription, cost approval |
| C-13 | Approve the phase C gate. | R-12 | C-01 to C-12 | Gate approved by a human |

## 6. Phase D: containment

Gate: verified coverage, visible uncertainties and independent runbook. Status: to do. Blocked by: phase C gate approved and a tenant with test agent identities.

Items by risk, the highest first.

| Id | Item | Risk | Tests | Blocked by |
| --- | --- | --- | --- | --- |
| D-01 | BT-14: obtain a token before the disable, disable and test the token on a test resource. Measure per identity type (agent identity, service principal, managed identity) and per resource. Report the real result. | R-02 | BT-14 | Tenant |
| D-02 | Real Entra disable adapter for agent identity. Validate the request body and the effect of `accountEnabled` (L-02, L-03). | R-02 | BT-13, BT-14 | Agent ID Administrator |
| D-03 | Real adapter for removing Selected grants, with verification by probe. Measure the delay until refusal. Handle the create and remove asymmetry (L-21). | R-07, R-12 | BT-07 | Tenant |
| D-04 | Runtime and managed identity isolation without disable: stop the runtime, remove grants, rotate credentials where applicable. | R-03 | BT-14 | Test runtime |
| D-05 | Real BT-13: disable one agent identity and verify that the sibling identities of the blueprint and the agent user keep the expected state. Measure the cascade (L-05, L-08). | R-13 | BT-13 | Blueprint with two agent identities |
| D-06 | Study the relationship between the block in Agent 365 and the disable in Entra (L-06). | R-11 | none | Licensed Agent 365, if approved |
| D-07 | Real throttling: partial states visible, `Retry-After` honoured. | R-22 | BT-12 | Tenant |
| D-08 | Containment report per resource, with `VerifiedScoped` only with explicit scope and probe. | R-16 | BT-11, BT-12 | D-02 to D-04 |
| D-09 | Break-glass independent of the application: document and rehearse with the application down. | R-20 | BT-20 | Runbook |
| D-10 | Runbook that allows an independent person to repeat the tests. | R-16 | all | D-01 to D-09 |
| D-11 | Real reconciliation: observed grants against recorded ones, broad grants, direct accesses. | R-12 | BT-15 | D-03 |
| D-12 | Approve the phase D gate. | R-02 | D-01 to D-11 | Gate approved by a human |

## 7. Phase E: pilot

Gate: human approval for production. Zero critical risks that are unmitigated or not formally accepted. Status: to do.

Items by risk, the highest first.

| Id | Item | Risk |
|---|---|---|
| E-01 | Formally decide all the risks in `risks.md` (accept, mitigate or block), with a named decision maker. | All |
| E-02 | Independent security review of the whole set. | R-06, R-12 |
| E-03 | Recovery: restore, failover, RPO and RTO approved and rehearsed. | R-21 |
| E-04 | Retention and deletion: policy approved and applied before the lock in production. | R-05 |
| E-05 | Real costs against the estimate. | R-15 |
| E-06 | Load, with the limit that load tests are not permitted on SharePoint Online [M31]. | R-22 |
| E-07 | Operation: minimum alerts (direct access discovered, drift, attempt after suspension, account without an owner, broad grant, containment failure, ledger or digest unavailable, sensitive administrative change), owners and rehearsal. | R-24, R-18 |
| E-08 | Approve the phase E gate. | All |

## 8. Definition of done for the MVP

Section 18 of the specification. The MVP is complete only when a real agent, in a test environment, reads only one authorised resource, and:

- all relevant actions have evidence;
- suspension blocks new admissions;
- the runtime and the grants are handled according to the adapters;
- the limits of existing tokens are measured;
- alternative accesses are eliminated or explicitly classified;
- an independent person can repeat the tests from the runbook.

An API with a 200 response, a green dashboard or a demonstration with mocks do not prove containment. Today (phase B) none of these conditions is proven with a real agent.

## 9. Sources

All verified on 2026-11-11. Prefix: `https://learn.microsoft.com/en-us/`. "Reading" indicates whether the page was read in full or only as a search excerpt.

| Key | Path | Reading |
|---|---|---|
| M09 | `entra/identity/managed-identities-azure-resources/managed-identities-faq` | full |
| M14 | `microsoft-agent-365/developer/identity` | full |
| M15 | `microsoft-agent-365/developer/observability-concepts` | full |
| M16 | `microsoft-agent-365/overview` | full |
| M18 | `graph/api/resources/agentidentity?view=graph-rest-1.0` | full |
| M22 | `graph/permissions-selected-overview` | full |
| M31 | `sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online` | full |
| M32 | `exchange/permissions-exo/application-rbac` | full |
| M34 | `azure/api-management/genai-gateway-capabilities` | full |
| M35 | `azure/api-management/validate-azure-ad-token-policy` | full |
| M39 | `sql/relational-databases/security/ledger/ledger-overview` | full |
| M40 | `sql/relational-databases/security/ledger/ledger-database-verification` | full |
| M41 | `sql/relational-databases/security/ledger/ledger-digest-management` | full |
| M44 | `sql/relational-databases/security/ledger/ledger-append-only-ledger-tables` | full |
| M46 | `azure/storage/blobs/immutable-storage-overview` | full |
| M49 | `azure/confidential-ledger/limits` | full |
| M60 | `entra/agent-id/what-is-microsoft-entra-agent-id` | full |
