# Nature of costs and tier decisions

Status: draft to be validated on the tenant

Verified against official documentation on 2026-11-11

## 1. Scope and rule of this document

This document answers the requirement in the specification (section 17) to justify tiers, network, support and costs before provisioning. It contains no monetary values and no numeric estimates. This is intentional.

Without a subscription or a tenant, any number would be invented. For each service, the document describes the nature of the cost (what is paid for), what makes it grow, the tier decision still to be made and the official pricing page. The value comes from the official pricing page and the official calculator (`https://azure.microsoft.com/pricing/calculator/`), after the decisions below have been made.

Project status, without overstatement: phase A in progress (this document), phase B implemented locally without real credentials, phases C, D and E not executed. The Bicep is written and has never been applied. Nothing has been provisioned, so there is no observed cost.

Legend: Verified (text read on Microsoft Learn on 2026-11-11), Inference (conclusion drawn from verified facts) and NOT VERIFIED (the documentation read does not state it). The pricing page URLs responded successfully on 2026-11-11, but their content was not read. No value is taken from them in this document.

## 2. Summary table

| Service | What is paid for | Tier decision still to be made | Official pricing page |
| --- | --- | --- | --- |
| Azure SQL Database | Compute, storage and backup redundancy | Purchase model, provisioned or serverless compute, zone and backup redundancy | `https://azure.microsoft.com/pricing/details/azure-sql-database/single/` |
| Storage with immutability | Capacity, operations and redundancy | Redundancy (not LRS), versioning, retention period before the lock | `https://azure.microsoft.com/pricing/details/storage/blobs/` |
| Service Bus | Fixed or variable pricing depending on the tier | Standard or Premium (Basic has no duplicate detection) | `https://azure.microsoft.com/pricing/details/service-bus/` |
| Key Vault | Operations and tier | standard or premium | `https://azure.microsoft.com/pricing/details/key-vault/` |
| Container Apps | Resources used, or instances allocated per profile | Consumption or Dedicated, minimum and maximum replicas | `https://azure.microsoft.com/pricing/details/container-apps/` |
| API Management | Tier and deployed units | Tier, number of units, network mode | `https://azure.microsoft.com/pricing/details/api-management/` |
| Azure Monitor, Log Analytics, Application Insights | Data ingestion, retention and export | Workspace plan, retention, daily quota, log destination | `https://azure.microsoft.com/pricing/details/monitor/` |
| Confidential Ledger (option) | NOT VERIFIED | Whether to use it as a digest destination | `https://azure.microsoft.com/pricing/details/confidential-ledger/` |
| Entra, Agent 365, Workload Identities Premium licences | Per user or per assigned licence | Which licences, for how many users | Agent 365: `https://www.microsoft.com/microsoft-agent-365`. Entra: pricing page not verified |

## 3. Azure SQL Database

### Nature of the cost

Verified. The documentation describes two purchase models: vCore ("provides a choice between the provisioned or serverless compute tiers") and DTU. With provisioned compute, you pay for the compute that is always allocated. With serverless, compute scales and can pause, with `autoPauseDelay` and `minCapacity` as control properties. Storage and backup redundancy are separate dimensions (`requestedBackupStorageRedundancy`: Geo, GeoZone, Local, Zone). Source: Bicep reference for `Microsoft.Sql/servers/databases` at `https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/databases`.

Cost specific to the ledger feature: NOT VERIFIED. The documentation read does not state it.

### What makes it grow

- Storage size. Inference from verified facts: ledger tables do not allow old data to be deleted or `TRUNCATE`, and "deleted" objects remain physically in the database. Source: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits`. With `isLedgerOn` enabled, all tables become ledger tables and the updatable ones have a history table. Storage only grows.
- Ledger verification. The documentation says it can be a resource-intensive process on large databases. The frequency is left to the customer. Source: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-verification`.
- Zone and backup redundancy, and geo-replicas. If there are secondaries, digests are only issued for data that has already been replicated.
- Scheduling of the verification. With Entra-only authentication, Elastic Jobs is not supported. The documented option that remains is Azure Automation, which is another service with its own billing. Source: `https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication`.

### Tier decision still to be made

- Purchase model and compute (provisioned or serverless). The accepted `sku` values are not enumerated in the reference read (NOT VERIFIED). The documentation example uses `GP_Gen5_2` in T-SQL.
- `isLedgerOn` at database level or ledger table by table. It is irreversible: "the value of this property cannot be changed after the database has been created". It has a direct effect on storage growth.
- Zone redundancy and backup storage redundancy.
- Interaction between the automatic pause of serverless compute and the digest upload and verification: NOT VERIFIED.

## 4. Storage with immutability

### Nature of the cost

Verified. "There's no extra capacity charge for using immutable storage. Immutable data is priced in the same way as mutable data." With version-level immutability, "the bill might be higher because you enabled versioning". Modifying a retention policy (locking or extending) results in an "Other operations" operations charge. Source: `https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview`.

You pay for capacity, operations and redundancy.

### What makes it grow

- Number and size of the digest blobs. Verified: with automatic management, the digest is generated every 30 seconds if there are transactions. Inference: the volume grows with database activity. Source: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management`.
- Retention. Verified: with the locked policy, capacity cannot be released before the end of the retention, and the storage account cannot be deleted while there is a container with a locked policy or blobs present. The cost of the capacity is committed for the chosen period.
- Redundancy. Verified: automatic digest management does not accept LRS accounts. Source: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits`.
- Versioning, if version-level immutability is chosen.

### Tier decision still to be made

- Account redundancy (any except LRS) and access.
- Container-level or version-level immutability. For the digests, the documentation uses a container policy with `allowProtectedAppendWrites`.
- Retention period before the lock. Verified: after the lock it can only be increased, at most five times. It is also a cost decision, because the capacity is committed.
- Who performs the lock and when. It is not confirmed that the lock can be declared in Bicep.

## 5. Service Bus

### Nature of the cost

Verified. By tier. The documentation states Premium with fixed pricing ("Fixed pricing") and Standard with variable pricing depending on consumption ("Pay as you go variable pricing"). Premium is sized in messaging units (`sku.capacity`). Duplicate detection requires Standard or Premium. Sources: `https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection` and `https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-azure-and-service-bus-queues-compared-contrasted`.

### What makes it grow

- Message and operation volume, on the Standard tier.
- Messaging units, on the Premium tier.
- Dead-letter queue. Verified: it has no automatic cleanup. Messages stay until they are read and completed. Source: `https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues`.
- Duplicate detection with a wide window. Verified: "Enabling duplicate detection and the size of the window directly impact the queue (and topic) throughput".
- Session state. Verified: it counts towards the entity's storage quota.

### Tier decision still to be made

- Standard or Premium. Basic is not suitable because it has no duplicate detection.
- Duplicate detection window (default 10 minutes, between 20 seconds and 7 days) and `maxDeliveryCount` (default 10).
- Sessions per tier: availability NOT VERIFIED.
- Private network for Service Bus and the tier that allows it: NOT VERIFIED.
- Zone redundancy. Verified: enabling `zoneRedundant` creates a Premium namespace in regions with availability zones.

## 6. Key Vault

### Nature of the cost

Verified. By tier (`standard` or `premium`). The overview refers to the pricing page for "pricing and feature comparisons between tiers". The per-operation detail was not read on Microsoft Learn (NOT VERIFIED).

### What makes it grow

- Number of operations on secrets, keys and certificates.
- Inference: if the design avoids secrets, using managed identities and federation, usage is only for exceptions. The specification (section 13) restricts Key Vault to exceptions that require secrets.
- Soft delete and purge protection. Verified: `enableSoftDelete` does not go back to false after being true, and `enablePurgeProtection` is irreversible. `softDeleteRetentionInDays` accepts 7 to 90. A deleted vault holds its name during the retention.

### Tier decision still to be made

- standard or premium. The functional difference between the tiers was not read in this research.
- Whether the pilot needs a vault, or whether secretless identities are enough.
- Private network for the vault (`publicNetworkAccess`).

## 7. Container Apps

### Nature of the cost

Verified. "Consumption plan: Serverless compute option where you're only billed for the resources your apps use as they're running." "Dedicated plan: Customized compute options where you're billed for instances allocated to each workload profile." With a dedicated profile there is a "Dedicated plan management fee". Source: `https://learn.microsoft.com/en-us/azure/container-apps/billing`.

### What makes it grow

- Resources consumed while replicas run, on the Consumption plan.
- Instances allocated per workload profile, on the Dedicated plan.
- Number of replicas. Verified: `maxReplicas` defaults to 10 if not set. How `minReplicas` affects billing on each plan: NOT VERIFIED.
- Log ingestion, when the environment's log destination is Log Analytics. See section 9.

### Tier decision still to be made

- Consumption or Dedicated. The `workloadProfileType` values are not enumerated in the reference read.
- A single app with the three services (the project image has a single artefact with ControlApi, PolicyExecutor and ContainmentWorker) or one app per service. The isolation of execution identities per service weighs on the decision.
- Minimum and maximum replicas.
- Environment network (`vnetConfiguration`) and zone redundancy.

## 8. API Management

### Nature of the cost

Verified. By tier, with the `sku.name`: Consumption, Developer, Basic, BasicV2, Standard, StandardV2, Premium, Isolated. By deployed units (`sku.capacity`). Consumption requires capacity 0. Source: `https://learn.microsoft.com/en-us/azure/templates/microsoft.apimanagement/2024-05-01/service`.

The nature of the cost per tier (fixed component, per-call component) was not read on Microsoft Learn (NOT VERIFIED). The pricing page settles this. The decision is not made without it.

### What makes it grow

- Chosen tier and number of units.
- Network. Verified: outbound virtual network integration is documented for the v2 tiers. An inbound private endpoint limits inbound traffic and disables public access. Which tiers support each network mode: NOT VERIFIED.
- AI gateway capabilities. Verified: "Capability availability varies by API Management service tier". Source: `https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities`.
- Logging of requests and responses, which feeds the Azure Monitor cost (section 9).

### Tier decision still to be made

- Tier that supports the required policies (`validate-azure-ad-token`, backend restriction) and the chosen network mode.
- Number of units and redundancy.
- Justify APIM against the value it brings to this product. Inference from the AI gateway page: APIM does not cover fine-grained authorisation of Graph access. The real control is the executor holding the credential. APIM enters through the Microsoft-first rule of the specification (section 3) and through token validation at the entry point. It is a cost and value decision to be approved.

## 9. Azure Monitor, Log Analytics and Application Insights

### Nature of the cost

Verified. "Azure Monitor uses a consumption-based pricing (pay-as-you-go) billing model where you only pay for what you use." "Log data ingestion is the largest component of Azure Monitor charges for most customers." You pay for ingestion, retention and export. Source: `https://learn.microsoft.com/en-us/azure/azure-monitor/fundamentals/cost-usage`.

### What makes it grow

- Volume of ingested logs. Sources: Container Apps logs (when the destination is Log Analytics), APIM logs, executor and worker logs, digest and verification metrics, SQL diagnostics.
- Retention (`retentionInDays`, "Allowed values are per pricing plan").
- Application Insights. Verified: in version `2020-02-02` of `Microsoft.Insights/components`, `WorkspaceResourceId` is required, so Application Insights writes to a Log Analytics workspace and shares its billing.
- Agent 365 telemetry does not go through Azure Monitor. It has its own licensing (section 11).

### Tier decision still to be made

- Workspace plan. The accepted values are `CapacityReservation`, `Free`, `LACluster`, `PerGB2018`, `PerNode`, `Premium`, `Standalone` and `Standard`. Which to use: to be decided.
- Retention per table and long-term destination.
- Daily quota (`workspaceCapping.dailyQuotaGb`). The effect of reaching the quota on the data that feeds security alerts: NOT VERIFIED. Do not use the quota as a cost control on alert telemetry without deciding that effect.
- Log destination of the Container Apps environment: `log-analytics`, `azure-monitor` or `none`.
- Reminder from the specification (section 12): Application Insights and Monitor do not replace the ledger.

## 10. Confidential Ledger (option)

### Nature of the cost

NOT VERIFIED on Microsoft Learn. It does not appear in the pages read. The official pricing page is `https://azure.microsoft.com/pricing/details/confidential-ledger/`. The path `/azure-confidential-ledger/` responded 404.

### What makes it grow

Not verified. What the documentation says about limits, which can influence the design and the indirect cost:

- 2 ledgers per subscription by default and entries of 8 KiB at most. Source: `https://learn.microsoft.com/en-us/azure/confidential-ledger/limits`.
- Mandatory replication to the regional pair, which cannot be changed or disabled. Source: `https://learn.microsoft.com/en-us/azure/confidential-ledger/secure-confidential-ledger`.
- The type (private or public) does not change after creation. Deletion is a "hard delete".

### Decision still to be made

- Whether to use it as a digest destination. The documentation presents it as "stronger integrity guarantee for customers who might be concerned about privileged administrators access to the digest" (`https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management`). Inference: it is justified if the threat model includes subscription or provider administrators with access to the digest storage.
- Specification baseline: immutable Blob with a locked policy. Confidential Ledger is an optional evaluation (section 11).
- The comparison table between immutable Blob and Confidential Ledger, referred to by the digests page, was not located: NOT VERIFIED.

## 11. Licences: Entra, Agent 365 and Workload Identities Premium

### Nature of the cost

Verified. Licences are per user or per assigned licence, not per Azure resource.

- Agent 365: "licensed on a per-user basis". At least one user must have a qualifying licence assigned. It is included in Microsoft 365 E7 and available as an add-on for E5, A5 and Business Premium. Source: `https://learn.microsoft.com/en-us/microsoft-agent-365/overview`.
- Entra Agent ID platform: "available for all Microsoft Entra customers". Source: `https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id`.
- Conditional Access for agents: Microsoft 365 E7, or Agent 365 with at least Entra P1 or Microsoft 365 E3. Another page says only Entra ID P1. The contradiction is flagged in `environment-assessment.md`.
- Conditional Access for service principals: Workload Identities Premium. Source: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity`.
- ID Protection for agents: P2 during the preview, and it will come to require Agent 365 (no date).
- Prices: NOT VERIFIED. The Microsoft Learn pages refer to `https://www.microsoft.com/microsoft-agent-365`, outside Learn. The pricing page for Entra and Workload Identities Premium was not verified in this research. Locate it before use.

### What makes it grow

- Number of users to license, per licence.
- Choice between E7 (which bundles Agent 365 and Entra Suite) and an add-on to an existing licence.
- Inference: a separate test tenant may require its own licences.

### Decision still to be made

- Whether the pilot needs Conditional Access for agents, or whether containment is done only with deactivation and removal of grants. This changes which licences are mandatory.
- Whether Agent 365 telemetry is part of the pilot. Without an assigned licence, spans are rejected with `200 OK`.
- Whether to accept or avoid dependency on preview features (ID Protection for agents, the "Agent execution environments" condition). Specification, section 0, point 8.

## 12. Other items without verified cost

- Managed identities: nature of the cost NOT VERIFIED (not consulted).
- Azure Automation, if chosen to schedule the ledger verification: own billing, not analysed.
- Private networks and private endpoints (APIM, Service Bus, Key Vault, SQL, Storage): cost and tier requirements NOT VERIFIED.
- Defender, Purview and Sentinel: optional add-ons in the specification (section 12). Subject to licensing and coverage. No cost analysis at this stage.
- Separate production and test environments: they duplicate the services above.

## 13. Questions to answer before provisioning

1. What is the approved budget for the pilot and who approves it?
2. Does the pilot run on a dedicated test tenant and subscription, or on an existing one? If dedicated, which licences must that tenant have?
3. Which region and which paired region will be used, and are all the services in the list available in those regions?
4. What is the data classification of the pilot site, and which personal data is excluded from the ledger and from immutable storage?
5. Is `isLedgerOn` set at database level or table by table? The decision is irreversible and defines storage growth.
6. What is the immutability period before the lock, approved by whom, and with what accepted impact on committed capacity?
7. Which digest account redundancy (not LRS) and database backup redundancy is approved?
8. How and how often is the ledger verified, given that Elastic Jobs is not supported with Entra-only? Azure Automation or another option?
9. Does Confidential Ledger enter the pilot? Does the threat model include administrators with access to the digest storage, and is the mandatory replication to the regional pair acceptable?
10. Service Bus Standard or Premium? Which duplicate detection window and which `maxDeliveryCount`? Who empties the dead-letter queue?
11. Container Apps on Consumption or Dedicated? One app or one per service? Minimum and maximum replicas?
12. Is APIM needed in the pilot? If so, which tier, which units and which network mode, and does the value it brings justify the cost compared with validating the token in the executor?
13. Which Log Analytics workspace plan, which retention and which daily quota policy, without losing telemetry that feeds security alerts?
14. Is Key Vault needed, or do secretless identities cover the pilot? Which tier?
15. Which Entra and Agent 365 licences are needed for the pilot scope, for how many users, and who approves them?
16. Is the Conditional Access licence contradiction for agents (Entra ID P1 against Agent 365) resolved on the tenant?
17. Which preview features does the pilot accept using, with which documented alternative and with which formal acceptance of the risk?
18. What is the maximum acceptable monthly cost during the pilot, and which cost alerts (budgets) are created before the first resource?
