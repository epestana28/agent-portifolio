# Risk register

Project: Agent Portifolio. Phase A document. Verification date of the Microsoft sources: 2026-11-11.

State of the project, without exaggeration: phase A was written from official documentation and without a tenant. Phase B (local vertical slice) is implemented in .NET 8, without real credentials, with the Microsoft world simulated. Phases C, D and E have not been executed. The Bicep is written and has never been applied. There is no real adapter for Entra or Graph. No risk in this register has been formally accepted: all the decisions below are still to be made.

## 1. Scales and conventions

Probability and impact are qualitative (low, medium, high). There are no numbers, because there is no data to support them.

- Probability: low means it requires an unusual sequence of events. Medium means it happens in normal operation without additional control. High means it is a known property of the platform or of the design.
- Impact: high means it can allow improper access to data, or make it impossible to prove containment. Medium means it degrades operation or evidence without opening access. Low means a recoverable inconvenience.
- Critical risk (proposal, to be approved at the phase A gate): high impact with medium or high probability, without a mitigation proven by test. The phase E gate requires zero critical risks that are unmitigated or not formally accepted.

States used:

- open: no proven mitigation.
- mitigated in the design, to be proven: the design has the control, the real test (phases C or D) is missing.
- blocked: the corresponding milestone does not advance until there is a decision or proof.
- to be decided: depends only on a human decision.

Decision required: accept, mitigate or block. "Decision maker" stays "to be assigned" in all cases: the assignment is a decision for the project owner.

Each Microsoft fact carries a key `Mxx` that points to the sources table at the end (Microsoft Learn URL and date 2026-11-11). The identifiers `L-nn` and `P-nn` point to `gaps-and-assumptions.md`. The `T-nn` and `BT-nn` point to `threat-model.md`.

## 2. Register

| Id | Description | Prob. | Impact | Mitigation | Status | Decision required |
|---|---|---|---|---|---|---|
| R-01 | Dependency on preview features or beta APIs. In preview according to the documentation: ID Protection for agents, blueprint manifest editor, "Agent execution environments" condition, Agent ID lifecycle policies (excerpt), agent user in Agent 365 (Frontier). In beta: Conditional Access policies for agents and agent sign-in logs, `isDisabled` deactivation, removal of permissions on list and item. No GA statement: Selected per level, RBAC for Applications, ledger in Azure SQL. Microsoft writes that preview has no service level agreement and "aren't recommended for production workloads" [M61] [M06] [M04] [M14] [M05] [M12] [M32] [M22]. | medium | high | ADR 0009: no preview feature is a mandatory dependency. Each one has a documented alternative. Explicit acceptance per feature before it enters the critical path. | to be decided | Accept per feature, with the documented alternative. Block any milestone that depends on a preview without acceptance. |
| R-02 | No guarantee about tokens already issued. Disabling an identity and blocking issuance does not invalidate existing access tokens: "the user loses access when the access token expires" [M01]. Default lifetime: 60 to 90 minutes [M07] [M08], or 1 hour [M01]. CAE covers only some resources and events: users up to 28 hours [M02], workload identities up to 24 hours, Graph only, single-tenant line-of-business applications only and only with `cp1` [M03]. CAE for agents: NOT VERIFIED [M04]. | high | high | Blocking new admissions comes from the authoritative state in the gateway and not from token validity (T-13). Assume validity until expiry (90 minutes worst case without CAE). BT-14 measures the real result per resource. The report shows explicit scope and limitation, never "universal access revoked". `Suspended` never becomes `VerifiedScoped` on its own. | mitigated in the design, to be proven (BT-14 only in phase D) | Accept the residual, with declared scope, after BT-14. Do not accept before measuring. |
| R-03 | Managed identities without documented disable and with token cache. There is no disable procedure through `accountEnabled` for managed identities (NOT VERIFIED). The token cache in the service is about 24 hours, with no forced refresh. Tokens of a deleted identity stay valid to the end. They are not covered by Conditional Access or by CAE [M09] [M10] [M03]. The documentation recommends federating the blueprint on a managed identity, and the effect on revocation is NOT VERIFIED [M14]. | medium | high | Separate adapter for managed identity. Containment by stopping or isolating the runtime, removing grants and, where applicable, rotating credentials. Do not delete identities as a first action. Do not share the same managed identity between runtimes with incompatible scopes. The revocation window is declared in the coverage. | open | Mitigate (isolation and removal of grants). Block the use of a shared managed identity as an agent identity. |
| R-04 | Licence contradiction. Conditional Access for agents: one page requires Entra ID P1, the most recent requires Microsoft 365 E7 or Agent 365 with Entra P1 or Microsoft 365 E3. ID Protection for agents: P2 during the preview, "starting soon" Agent 365. Prices and licence per operation: NOT VERIFIED [M04] [M05] [M06] [M62]. | high | medium | Treat Agent 365 as a requirement until proven otherwise. Document the divergence. Cost estimate without values, only with the nature of the cost and the official pricing page. Confirm in the tenant. | open | Block phase C until the licences are confirmed and approved (phase A gate: licences). |
| R-05 | Immutability versus GDPR. Append-only ledger tables do not allow deleting data ("Deleting older data in append-only ledger tables ... isn't supported", `TRUNCATE` not supported) [M42]. A `locked` immutability policy cannot be deleted, only increased, and the account cannot be deleted while there is a container with a `locked` policy [M46]. `isLedgerOn` cannot be reverted [M42]. The documentation read does not address GDPR. There is no automatic GDPR compliance from using Microsoft services. | medium | high | No unnecessary personal data in the ledger or in immutable storage. The ledger stores the SHA-256 digest of the arguments and minimised references. Pseudonymisation, retention and deletion decided before the lock. Retention decided before the lock because afterwards it can only increase. Decision on `isLedgerOn` before creating the database (P-11). | blocked (lock and ledger database) | Block the policy lock and the creation of the ledger database until a human decision with the data protection officer. |
| R-06 | Bypass of the gateway by whoever edits APIM policies. Whoever has edit permission can use `authentication-managed-identity` to authenticate as the service identity, extract or forward the token, and APIM "does not validate which backend the token is sent to" [M37]. The validation policy can reference available certificates [M35]. There is no documented mechanism to restrict Graph to requests from an APIM (L-32). | medium | high | The Graph credential stays in the executor and not in APIM. The executor validates the token and the authoritative state by itself [M38]. APIM Azure RBAC separated from the data plane. Policies only through IaC with review and CI. Alert on sensitive administrative change. In case of doubt, the real control is that the agent has no Graph credential (BT-06). | mitigated in the design, to be proven (BT-05 and BT-06 in phase C) | Mitigate. Accept the residual for whoever controls the platform (boundary F8). Do not accept without separation of roles in Azure. |
| R-07 | Contradiction on the minimum scope of read operations. The Selected overview says that `Sites.Selected` with the `read` role serves the read paths. The four read pages list `Files.Read.All` as least privilege, with whole-tenant scope (L-19). If the overview is not confirmed, the narrow-scope MVP is not viable. | medium | high | The design assumes `Sites.Selected` with `read` (inference). Endpoint-by-endpoint test and negative test on another resource in phase C. Never use `Sites.Read.All`, `Sites.ReadWrite.All` or `Files.Read.All` to work around the prototype. | blocked (real read implementation) | Block the real implementation of the four operations until the matrix is complete in the tenant (the specification blocks incomplete rows). |
| R-08 | Pre-authenticated download URL. `GET .../content` returns 302 to a URL that does not require authentication [M25]. Removing permissions "might not immediately invalidate the URL" [M28]. Validity: minutes [M25] or 1 hour [M28]. | medium | medium | The executor follows the redirect, returns only bytes, does not return or log the `Location` or the URL, and removes `downloadUrl` from the metadata. Worst case of 1 hour in the analysis. List of egress hosts of the executor. | mitigated in the design, to be proven (phase C) | Accept the residual (window of up to 1 hour per URL already issued) and declare it in the containment documentation. |
| R-09 | Bypass of path lists through `$batch`, `$expand` and redirects. Batch: 20 requests per call, outer method always POST, outer `200` does not show the inner results [M29]. `$expand` brings children in the same call [M26]. | medium | high | No batch operation in the catalogue. The executor builds the requests. APIM denies `/$batch` (v1.0 and beta). Closed arguments, no `$expand` or `$select`. TC-06. | mitigated in the design, to be proven (TC-06 local, BT-05 phase C) | Mitigate. Any future composite request requires validation of each inner request against the same list. |
| R-10 | Union of Entra permissions with Exchange RBAC for Applications. "The permissions assigned using Application RBAC act in addition to grants you make in Microsoft Entra ID." An unscoped `Mail.Read` permission in Entra cancels the scope of the RBAC [M32]. There is no drafts-only role [M32]. Propagation of 30 minutes to 2 hours. Outside the MVP. | medium | high | Verify both authorities (Entra and Exchange) before classifying a mail capability as scoped. Remove organisation-wide permissions from Entra. Restrict operations in the executor. Do not use Application Access Policies in new implementations [M33]. | blocked (mail, later phase) | Block mail until there is verification of both authorities and a decision on the GA status of RBAC (L-28). |
| R-11 | Two blocking switches without documented synchronisation: blocking in the Microsoft 365 admin centre (Agent 365) and disabling in Entra. Whether one changes the other is NOT VERIFIED [M64]. | medium | medium | The containment report treats each switch as a separate step, with verification by its own probe. Test in the tenant if Agent 365 is licensed. | open | Mitigate. Decide whether Agent 365 is in scope. |
| R-12 | Alternative accesses of the agent to the resource. Direct grants on the agent, shared credentials, local paths, or a runtime with a route to Graph. The portfolio cannot remove on its own an external grant used by a direct path. | medium | high | Classified coverage (`GatewayEnforced`, `DirectAccessPossible`, `Unverified`). Grants on the agent lower the coverage. The strong profile removes direct accesses and restricts egress, as additional defence. Periodic reconciliation (T-12). | mitigated in the design, to be proven (BT-05 and BT-06 in phase C) | Block the `GatewayEnforced` classification until there is proof that direct access is impossible in the strong profile. |
| R-13 | Blueprint credentials shared by all child identities. Compromise of the blueprint affects all of them. Disabling the blueprint affects all of them [M14] [M06]. | low | high | Each blueprint is a credential boundary. Prefer federated credential to secret. Disabling a blueprint requires a distinct approval and scope. Disabling the individual agent is the default. | open | Mitigate. Decide how many agents per blueprint in the pilot. |
| R-14 | Trust in the digest destination. An administrator with access to the destination can tamper with digests and data together [M39]. The ledger gives evidence of tampering, not prevention. | low | high | Blob with a `locked` policy and writes only by the server identity. SQL privileges, destination and verification kept separate. Periodic verification and after restore. Azure Confidential Ledger as an option with a stronger guarantee against privileged administrators [M41], with its own limits [M49]. | to be decided | Decide between immutable Blob and Confidential Ledger as the destination. Accept the Blob residual if the threat model does not include subscription administrators. |
| R-15 | Unknown costs. The nature of the cost is described. The values are not: APIM per tier, SQL, Confidential Ledger, Key Vault and managed identities are partly NOT VERIFIED. A `locked` policy prevents releasing capacity before the end of retention [M46]. | medium | medium | Cost estimate only with the nature of the cost and the official pricing page, without values. Justify tiers, network and support before provisioning. | to be decided | Block provisioning until there is an approved estimate. |
| R-16 | Simulation mistaken for proof. The Microsoft world is simulated in phase B. BT-05, BT-06, BT-14, BT-17 and BT-20 are only proven in phases C and D. A 200 response, a green dashboard or a demonstration with mocks do not prove containment. | medium | high | Simulation marked throughout the code and documentation. Status of the BT explicit in the threat model. The containment report distinguishes administrative confirmation from confirmation by probe. | mitigated in the design | Mitigate. No report may claim containment without proof from phases C and D. |
| R-17 | Assumptions without a tenant. Format of identifiers, `tid` and `oid` claims, behaviour of `accountEnabled`, hosts of the 302 (P-01, P-02, P-07, P-09). | medium | medium | Documented as assumptions. They fail closed when wrong (refuse). Validation in phases C and D. | blocked (phase C) | Block phase C until there is a tenant. Validate each assumption and update `gaps-and-assumptions.md`. |
| R-18 | Operation of the dead-letter queue and the outbox. The Service Bus DLQ does not clean itself [M52]. Redelivery is at least once [M50]. A message held in the DLQ is a containment still to be handled. | medium | medium | Alert on DLQ depth. Handling process in the runbook. Idempotent consumers and epoch revalidation. | mitigated in the design, to be proven (phase C) | Mitigate. Define who handles the DLQ. |
| R-19 | Scheduling of ledger verification with Entra-only authentication. Elastic Jobs is not supported with Entra-only [M53], and verification is heavy on large databases [M40]. | medium | medium | Azure Automation or a dedicated job, to be decided. Frequency chosen by the customer [M40]. Verification identity with only `VIEW LEDGER CONTENT`. | to be decided | Decide the scheduling mechanism and the frequency. |
| R-20 | Compromised operator or PolicyAdmin, or collusion of two PolicyAdmins. A SQL or Azure administrator outside the application is not stopped by it. | low | high | Separate roles. Reactivation by two distinct PolicyAdmins. Human initiator in each event. Temporary privileged access when licensed. Ledger and digests for evidence. | mitigated in the design, to be proven (BT-19 local, TC-09) | Accept the collusion residual. Require strong authentication in the tenant. |
| R-21 | A restore or failover brings back a state in which the agent was active. | low | high | BT-17. Reconciliation and RPO validation before reopening traffic. Ledger verification after restore. Digests only of replicated data [M41]. Restore and failover rehearsal. | mitigated in the design, to be proven (phase C) | Mitigate. Approve RPO and RTO before phase E. |
| R-22 | Throttling and application blocking in SharePoint. 429 or 503 with mandatory `Retry-After`. SharePoint can block the application or request patterns. Limits depend on the licences. Load tests are not permitted [M31]. The documentation warns against splitting a function across several AppIDs to avoid the limits [M31], and the design has one execution identity per agent (inference of tension, to be validated in the pilot). | medium | medium | Adapters and executor honour `Retry-After`. Partial state visible under throttling. Decorated User-Agent. Hourly limit per agent. Measure in the pilot, without artificial load on SharePoint. | mitigated in the design, to be proven (phase C) | Accept the residual. Confirm in the pilot that per-agent identities do not degrade the tenant limits. |
| R-23 | SQL availability is the availability of the product. Fail-closed denies new actions when the authoritative state is not available (T-16). | medium | medium | Deliberate choice. High availability and geo-replication of the database. Alerts. | mitigated in the design | Accept, with an availability SLO to be defined. |
| R-24 | Agent 365 telemetry fails silently: `200 OK` with rejected spans (`tenant_not_licensed`), only `invoke_agent` in the admin centre, retention and latency NOT VERIFIED [M15]. | medium | low | Agent 365 observability complements and does not replace the ledger. Check rejections in the per-span results. | open | Accept. Do not use as primary evidence. |
| R-25 | Owners of the threats and risks not assigned. Without an owner, a mitigation has nobody responsible. | high | medium | Each threat indicates the suggested role. | to be decided | Block the phase A gate until assigned. |

## 3. Risks that block milestones

| Risk | Blocked milestone | Reason |
|---|---|---|
| R-04 | Phase C | Licences to be confirmed. |
| R-05 | Lock of the immutability policy and ledger database | Data protection decision. |
| R-07 | Real implementation of read operations | Permission matrix incomplete. |
| R-10 | Scoped mail (later phase) | Verification of both authorities. |
| R-12 | `GatewayEnforced` classification | Proof that direct access is impossible. |
| R-15 | Provisioning | Cost estimate to be approved. |
| R-17 | Phase C | No tenant. |
| R-25 | Phase A gate | Owners to be assigned. |

## 4. Sources

All verified on 2026-11-11. Prefix: `https://learn.microsoft.com/en-us/`. "Reading" indicates whether the page was read in full or only as a search excerpt.

| Key | Path | Reading |
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
| M12 | `entra/identity/enterprise-apps/deactivate-app-registration` | full |
| M14 | `microsoft-agent-365/developer/identity` | full |
| M15 | `microsoft-agent-365/developer/observability-concepts` | full |
| M22 | `graph/permissions-selected-overview` | full |
| M25 | `graph/api/driveitem-get-content?view=graph-rest-1.0` | full |
| M26 | `graph/api/driveitem-get?view=graph-rest-1.0` | full |
| M28 | `graph/api/resources/driveitem?view=graph-rest-1.0#instance-attributes` | excerpt |
| M29 | `graph/json-batching` | full |
| M31 | `sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online` | full |
| M32 | `exchange/permissions-exo/application-rbac` | full |
| M33 | `exchange/permissions-exo/application-access-policies` | full |
| M35 | `azure/api-management/validate-azure-ad-token-policy` | full |
| M37 | `azure/api-management/api-management-howto-use-managed-service-identity#security-considerations-for-managed-identities` | excerpt |
| M38 | `entra/msidweb/advanced/api-gateways#follow-best-practices` | excerpt |
| M39 | `sql/relational-databases/security/ledger/ledger-overview` | full |
| M40 | `sql/relational-databases/security/ledger/ledger-database-verification` | full |
| M41 | `sql/relational-databases/security/ledger/ledger-digest-management` | full |
| M42 | `sql/relational-databases/security/ledger/ledger-limits` | full |
| M46 | `azure/storage/blobs/immutable-storage-overview` | full |
| M49 | `azure/confidential-ledger/limits` | full |
| M50 | `azure/service-bus-messaging/service-bus-message-loss-and-duplicates` | excerpt |
| M52 | `azure/service-bus-messaging/service-bus-dead-letter-queues` | full |
| M53 | `azure/azure-sql/database/authentication-azure-ad-only-authentication` | full |
| M61 | `entra/id-governance/agent-lifecycle-policies` | excerpt |
| M62 | `entra/fundamentals/licensing` | excerpt |
| M64 | `microsoft-agent-365/admin/manage-agents` | excerpt |
