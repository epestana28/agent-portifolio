# Bicep infrastructure (Phase C, test environment)

## Actual status

This Bicep has never been applied. There is no Azure subscription associated with the project.

The syntax has not been validated by a compiler. Neither `bicep` nor `az bicep` exists on the machine where these files were written, and nothing was installed. The first step before any other is to run `bicep build main.bicep` and fix whatever the compiler reports.

The resource types, apiVersions and properties were taken from the Microsoft Learn research of 2026-11-11. The most recent versions (2026-xx) are documented, but it is not known whether the installed version of Bicep or every region accepts them. If the compiler or the deployment rejects them, the research lists earlier stable versions for each type, but the properties were only confirmed in the version in use here.

Everything that is not verified appears as a `// TO VALIDATE:` comment next to the line concerned. There are no invented values: what is not known stays in a comment or in a parameter with no default value.

Phases C, D and E have not been carried out. There is no real adapter for Entra or Graph. This Bicep creates the infrastructure where those adapters will run, and does not replace them.

## Why not to apply without the Phase A gates

Phase A is the approval gate for scope, licences and permissions. This Bicep makes decisions that depend on it and that are difficult or impossible to undo.

| Decision | Why it cannot be undone |
| --- | --- |
| `isLedgerOn` on the database | It cannot be changed after creation. With `true`, all tables become ledger tables. A ledger database does not go back to being a normal one, and a ledger table does not go back to being a normal one. |
| Retention of the immutability policy and its lock | After the lock the policy cannot be deleted, the period can only increase (a maximum of five increases at container level), and the storage account cannot be deleted before the retention expires. |
| Key Vault purge protection | It does not accept `false` once enabled. The vault name stays reserved during the retention period after deletion. |
| Entra-only authentication on SQL | It turns off SQL authentication, including for administrators. The generated account (`CloudSA...`) is not a fallback or a break-glass account. |
| Redundancy of the digests account | LRS is not supported for automatic digests. |
| Personal data | It cannot go into ledger tables or immutable storage, because there is no documented way to delete it. The decision on pseudonymisation, retention and deletion must exist before the lock. |

There are also cost decisions (SQL, APIM and Service Bus tiers) and network decisions (private endpoint, firewall, virtual network) that this Bicep deliberately leaves open.

Applying this Bicep also proves nothing about containment. A successful deployment, a green dashboard or a demonstration with simulated adapters do not prove containment. The Phase C gate is the proof that direct access is impossible in the strong profile, and this infrastructure alone does not provide it (see the note on the executor ingress).

## Structure

| File | What it creates |
| --- | --- |
| `main.bicep` | Orchestrates the modules. Parameters: region, prefix, tenant, administrators, image. No secrets and no sensitive default values. |
| `main.bicepparam.example` | Example with placeholders. Copy to `main.bicepparam` and fill in. |
| `modules/identities.bicep` | Three separate user-assigned identities: control-api, policy-executor, containment-worker. |
| `modules/monitoring.bicep` | Log Analytics workspace and Application Insights linked to the workspace. |
| `modules/keyvault.bicep` | Key Vault with RBAC authorisation, soft delete and purge protection. No secrets and no roles. |
| `modules/storage-digests.bicep` | Storage account and `sqldbledgerdigests` container with an UNLOCKED immutability policy. |
| `modules/sql.bicep` | Entra-only SQL server, database and, if enabled, the digest upload configuration. |
| `modules/servicebus.bicep` | Namespace without SAS and `containment` queue with duplicate detection, dead-letter and `maxDeliveryCount`. |
| `modules/containerapps.bicep` | Environment and three container apps, each with its own identity. Executor and control-api with ingress, worker without ingress. |
| `modules/apim.bicep` | APIM instance and an API for the executor with `validate-azure-ad-token`. |
| `policies/executor-inbound.xml` | Inbound policy template. The Bicep replaces the placeholders. |

### Identities

The control identity (`control-api`) is never assigned to the agent runtime. This Bicep does not create the identity of the agent runtime. That identity is created separately, with no access to the database, the Service Bus or the Key Vault of this application.

An identity is not reused between services with incompatible scopes.

### Digests container and unlocked policy

The policy created has the period given in `digestImmutabilityDays` and `allowProtectedAppendWrites` enabled (the digests are append blobs). It is deliberately left without a lock. An unlocked policy protects against deletion and overwriting, but it can be shortened or deleted, and the documentation recommends it only for short-lived tests.

The lock is a manual step, after the retention has been approved, for the reasons in the table above. The documented command is `az storage container immutability-policy lock`, with the `etag` of the policy obtained when creating or reading it, and the `--if-match` option. It is not confirmed that the lock can be declared in Bicep. Confirm the full syntax in the current documentation before running it.

The value `digestImmutabilityDays = 1` in the example file comes from the documentation quickstart. The documentation itself warns that a much larger value should be used in production.

### Executor ingress

The ingress of the `policy-executor` is external and not restricted to APIM. With this Bicep, the executor is reachable without going through the gateway. BT-05 (the gateway and backend cannot be bypassed) is not proved by this infrastructure. The minimum mitigation is for the executor to validate the token itself, which is a property of the code and not of the Bicep. The network restriction to APIM is TO VALIDATE: the `ipSecurityRestrictions` property exists, but its fields were not extracted. Resolve this in Phase C before declaring the strong profile.

In addition, Microsoft Graph is a public service and no client-side network mechanism restricts Graph to requests coming from APIM. Anyone with a valid Graph token calls Graph directly. What prevents the bypass is that the agent never holds the Graph credential, which stays only in the executor.

## Order of application

1. Close the Phase A gates and the decisions in the table above. Record each decision in an ADR.
2. Run `bicep build main.bicep` and fix syntax and type errors. Confirm the apiVersions against the compiler.
3. Create the resource group and the role assignments of whoever will apply the template. Both stay outside the Bicep.
4. Copy `main.bicepparam.example` to `main.bicepparam`, fill it in, and resolve each `TO VALIDATE` that affects the chosen parameters.
5. Run a deployment simulation (what-if) and review the result. Confirm the Azure CLI deployment commands in the current documentation before running them.
6. First deployment, with `enableLedgerDigestUpload = false`. Within it, the order is resolved by dependencies: monitoring and identities, Key Vault, digests account, SQL, Service Bus, container apps, APIM.
7. Manual role assignments (next section). Among them, the write role of the SQL server on the digests account.
8. Post-deployment T-SQL steps: create the database users for each identity, grant only the minimum, and enable `ALLOW_SNAPSHOT_ISOLATION` if it is not already enabled (needed by the verification procedures).
9. Second deployment with `enableLedgerDigestUpload = true`. Check that the digests start to appear (runbook 05).
10. Decide on and approve the retention. Only then lock the immutability policy (manual, irreversible step).
11. Configure the alerts for digest upload failure and for a drop in successful uploads. The alerts are not part of this Bicep.

## Assignments and settings that are deliberately left out

No Azure role assignment is in this Bicep. The resource type of the assignments and the role identifiers were not verified in the research, and assigning roles is a privilege decision that a person should review.

| Item | Why it is left out |
| --- | --- |
| `Storage Blob Data Contributor` role of the SQL server (system identity) on the digests account | Required by the digests documentation. In geo-replication or failover groups, the secondary replicas need the same role. Manual assignment, before turning on the upload. |
| T-SQL users of the identities (`CREATE USER ... FROM EXTERNAL PROVIDER`) | It is T-SQL and cannot be declared in Bicep. Principle of least privilege: write identity with only what is needed on the ledger tables, verification identity with only `VIEW LEDGER CONTENT`, and none with `ENABLE LEDGER` at runtime. The exact `GRANT` syntax of each ledger permission was not read: confirm in the current documentation. |
| Service Bus data roles for the identities | Role names not verified. The namespace has `disableLocalAuth`, so without a role nobody can send or receive. |
| Key Vault roles for the administrators | Role names not verified. The `adminObjectIds` beyond the first are for this manual assignment. |
| `AcrPull` or equivalent, if `registryServer` is set | Each app pulls the image with its own identity. The assignment is manual. |
| `Selected` grant on the SharePoint site (and the permission consent in Entra) | Consent alone does not give access: an assignment on the resource is needed. That assignment is made by an identity separate from the executor's, with the permission that the documentation requires for assigning (`Sites.FullControl.All` at site level), never by the `policy-executor` or the `control-api`. There is a contradiction in the documentation for list and item, so the stronger requirement is the one that applies until it is tested in a tenant. |
| Agent runtime identity and its accesses | Created separately. It receives no access to the resources of this application. |
| APIM authentication for the executor | Not configured. Whoever edits APIM policies can use the service's managed identity. Never use the APIM identity as a data credential. |
| Periodic ledger verification job | With Entra-only authentication, Elastic Jobs is not supported. The documentation points to Azure Automation. Outside the Bicep and to be defined in Phase C. |
| Private network, SQL and storage account firewall, private endpoint | Types not verified and a Phase A decision. Without a firewall rule or private endpoint, no client connects to SQL over the public network. The behaviour of the digest upload with a restricted network was not verified. |
| Variables for Service Bus, Key Vault and Application Insights in the apps | The current configuration contract only lists the requested variables. Add them when the code defines them. |
| Alerts, Azure Policy, Defender, Sentinel, Purview | Out of scope for this Bicep. |
| Break-glass accounts | Independent of this application, by definition (runbook 04). |

## Application environment variables

All apps receive `Storage__Provider=SqlServer`, `Storage__ConnectionString`, `Storage__EnsureSchema=false`, `Auth__Authority`, `Auth__Audience`, `Auth__AllowedTenants__0` and `Adapters__Mode`. Each app also receives `AP_SERVICE` with the value `control`, `executor` or `worker`.

`Adapters__Mode` can only be `Simulation`: the code refuses to start with any other value, because there are no real adapters for Entra or Graph. Applying this Bicep does not give real containment, it only gives the infrastructure where the simulation runs. The Bicep has never been applied.

`Storage__EnsureSchema=false` because the schema (`src/Storage.SqlServer/Schema/001_schema.sql`) creates a ledger table and that requires `ENABLE LEDGER`, which no runtime identity should have. The schema is applied once, with an administrative account, before the first start. In the code the default is `true`, so whoever removes this variable gives the startup the attempt to create the schema again.

The connection string contains no password: it uses a managed identity, with the `clientId` of each service's own identity. The code passes it unchanged to `Microsoft.Data.SqlClient`. The exact spelling for a managed identity is TO VALIDATE against the driver documentation and tested against Azure SQL in Phase C.

`Auth__Audience`, `Auth__Authority` and `Auth__AllowedTenants__0` also reach the worker only because the common contract lists them for the three services. The worker reads no `Auth__` variable and does not validate tokens, so they may be removed with no effect. The worker reads `Worker__PollSeconds` (default 2) and the executor reads `Execution__ExternalTimeoutSeconds` (default 10): neither is set here.

## What is TO VALIDATE

Consolidated list. Each item has the corresponding comment next to the code.

| Area | Item |
| --- | --- |
| Compilation | Syntax and types not validated by a compiler. 2026 apiVersions not confirmed against the installed Bicep. |
| Identities | Read-only properties `principalId` and `clientId`. |
| SQL | Server `identity`; `administratorType` in the `administrators` block; `sku.name` values; network access; use of the server's user-assigned identity for the digest upload; behaviour of the upload with a restricted network. |
| Digests and storage | Pre-creation of the `sqldbledgerdigests` container with a policy before the first upload; policy lock in Bicep; upload with shared keys turned off; `blobServices` type (avoided with full names). |
| Service Bus | Spelling of the `sku.name` values; changing `requiresSession` after creation; sessions per tier. |
| Key Vault | `accessPolicies: []` with `enableRbacAuthorization: true`; `publicNetworkAccess` values. |
| Container Apps | Format of the `identity` block; `template.containers[]`; workspace `listKeys()`; `ingress.fqdn`; executor ingress restriction; resources (cpu and memory). |
| APIM | `service/apis` and `service/apis/policies` types and properties; names and order of the child elements of `validate-azure-ad-token`; `POST /actions` operation (in a comment). |
| Monitoring | Accepted values of `retentionInDays`; semantics of `dailyQuotaGb`. |
| Token authority | The code uses the issuer from the metadata of `Auth__Authority`. The version of the issuer of the real tokens issued in the tenant is to be validated. |

## Nature of the costs

No values. The official calculator is https://azure.microsoft.com/pricing/calculator/ and each service has its own pricing page. The research confirmed that the pages exist, but did not read their content.

| Service | Nature of the cost | Pricing page |
| --- | --- | --- |
| Azure SQL Database | Compute (provisioned or serverless, with automatic pause and minimum capacity), storage and redundancy of the backups. Ledger tables are not purged, so storage only grows. Ledger verification consumes database resources. Cost specific to the ledger feature: not verified. | https://azure.microsoft.com/pricing/details/azure-sql-database/single/ |
| Immutable Blob Storage | Capacity, operations and redundancy. No additional capacity cost for immutability. Modifying or locking the policy counts as an operation. With a locked policy the capacity is not released before the end of the retention. | https://azure.microsoft.com/pricing/details/storage/blobs/ |
| Service Bus | By tier. Premium has a fixed price and scales in messaging units. Standard is pay per use. | https://azure.microsoft.com/pricing/details/service-bus/ |
| Key Vault | By tier and by operations. Detail per operation not verified in Microsoft Learn. | https://azure.microsoft.com/pricing/details/key-vault/ |
| Container Apps | Consumption plan, in which you only pay for what the apps use while they run. A dedicated profile pays for allocated instances and a management fee. This Bicep uses the consumption plan. | https://azure.microsoft.com/pricing/details/container-apps/ |
| API Management | By tier and by units. It is usually a significant fixed cost in the tiers with units, but the nature by tier was not read in Microsoft Learn. | https://azure.microsoft.com/pricing/details/api-management/ |
| Log Analytics and Application Insights | Consumption. Log ingestion is normally the largest component. Retention and export are also charged. Controlled by `retentionInDays` and `dailyQuotaGb`. | https://azure.microsoft.com/pricing/details/monitor/ |
| Managed identities | Nature of the cost not verified. | No verified page |

Licences for Entra Agent ID, Agent 365 and Conditional Access for agents are not part of this Bicep and have their own requirements, with contradictions between official pages. See the identity research.

## Sources

Verified in Microsoft Learn on 2026-11-11.

- Template reference, base: https://learn.microsoft.com/en-us/azure/templates/
- Ledger, digest management: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management
- Ledger, limits: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits
- Entra-only authentication in Azure SQL: https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication
- Immutable blob, container policies: https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-container-level-worm-policies
- Service Bus, duplicate detection: https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection
- Service Bus, dead-letter: https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues
- APIM, validate-azure-ad-token: https://learn.microsoft.com/en-us/azure/api-management/validate-azure-ad-token-policy
- Container Apps, billing: https://learn.microsoft.com/en-us/azure/container-apps/billing
