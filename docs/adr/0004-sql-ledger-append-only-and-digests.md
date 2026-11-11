# ADR 0004: SQL Ledger append-only and digests

Status: proposed. Source verification date: 2026-11-11.

## Context

A verifiable history of decisions and executions is needed: who asked, what was decided, in which version and epoch, with what result. The specification requires preferring append-only ledger tables in Azure SQL Database, with digests in independent protected storage, and forbids implementing a custom hash chain as a substitute.

Actual state: `ap.AuditEvent` is an append-only ledger table in `src/Storage.SqlServer`, exercised locally on SQL Server 2022 in a container (`BT16_`, with `AP_TEST_SQL`), with digests generated in the database itself. There is no Azure SQL database and no digest storage: the Bicep is written and has never been applied. By default the code tries to create the schema at start-up (`Storage:EnsureSchema`), which requires `ENABLE LEDGER`: in environments with point 4 of this decision, set it to `false` and apply the schema with an administrative account.

## Decision

1. Domain events in an append-only ledger table (`WITH (LEDGER = ON (APPEND_ONLY = ON))`). The state tables, which need `UPDATE`, are normal tables. This assumes that the database is not a ledger database (`isLedgerOn` off): assumption P-11, human decision in phase A.
2. Automatic digests to Azure Blob with a validated immutability policy, `locked` once the retention is approved. Redundancy other than LRS. Azure Confidential Ledger as an alternative destination, if the trust separation justifies it.
3. Periodic verification of the ledger against the digests, and after a restore or failover. Scheduling uses Azure Automation or a custom job, because Elastic Jobs is not supported with Entra-only authentication.
4. Separate privileges: one identity that only inserts events, one that only verifies (`VIEW LEDGER CONTENT`), and none with `ENABLE LEDGER` at run time. The concrete separation is an inference and remains to be validated.
5. The agent identity, the human initiator and the executing identity go in event columns, because the ledger `principal_name` is the original login of the connection (the application identity).
6. The ledger stores a SHA-256 digest of the arguments and minimised references, never tokens, secrets, content, prompts or unnecessary personal data.
7. Monitor the age of the last digest, export failures, the verification result and time coverage.

### Why not a custom hash chain

- SQL Ledger already hashes each transaction, groups them into blocks with the root of a Merkle tree and includes the hash of the previous block. A second chain inside the same database repeats this.
- It brings digests, a documented digest format, automatic upload to immutable storage and verification procedures (`sp_verify_database_ledger_from_digest_storage`). A custom chain would require rewriting all of this.
- A custom chain stored in the same database has the same limitation as any ledger: whoever controls the machine rebuilds the chain. It would only gain value with an external anchor, which is exactly the SQL Ledger digest (inference).
- Custom cryptographic code is error surface with no gain in guarantee.
- An auditor can use the documented Microsoft procedures instead of reviewing an implementation of the project.
- The specification expressly forbids it.

## Consequences

- The ledger gives evidence of tampering after verification against trusted digests. It is not prevention, does not stop a false initial entry, does not guarantee availability and does not recover tampered data.
- Without trusted external digests and periodic verification, the guarantee does not exist in practice.
- Nothing is deleted: no `TRUNCATE`, no deleting old data, dropped tables and columns remain physically. A `locked` policy can only be increased. Define pseudonymisation, retention and deletion before locking immutability (risk R-05). GDPR compliance is not claimed for using Microsoft services.
- Database ledger and table ledger are irreversible decisions. A normal table cannot be converted into a ledger table or the reverse.
- Limits to respect: 200 ledger tables per transaction, only new nullable columns, unsupported data types (`XML`, `SqlVariant`, user-defined types, `FILESTREAM`, `Vector`), no transactional replication.
- Because the ledger does not allow correcting a duplicate row, whoever writes events must be idempotent (ADR 0005).
- The verifier consumes database resources and requires `ALLOW_SNAPSHOT_ISOLATION`.
- Most of the real configuration has not been validated: policy lock in Bicep, pre-creation of the `sqldbledgerdigests` container, user-assigned identity for upload, behaviour with a private network.

## Rejected alternatives

- Custom hash chain: see above.
- A whole ledger database (`isLedgerOn`): all tables become ledger tables, operational ones stay updatable with a history table that cannot be deleted, and the choice is irreversible.
- A normal table with permissions, triggers and backups: an administrator changes the data with no verifiable trace.
- Azure Confidential Ledger as the main storage of the events: the documentation recommends it as storage for digests of relational data. It has 8 KiB entries, two ledgers per subscription by default, deleting is irreversible, it always replicates to the regional pair, and the model is not relational.
- Application Insights or Monitor as evidence: they do not replace the ledger.
- Immutable Blob without a ledger: loses per-transaction integrity verification.

## Documentary evidence

All verified on 2026-11-11.

- Tamper-evidence, not prevention: "an attacker or system administrator who has control of the machine can bypass all system checks ... Ledger can't prevent such attacks but guarantees that any tampering will be detected when the ledger data is verified". https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-overview
- Blocks close about every 30 seconds or at 100K transactions. `sys.database_ledger_blocks` stores the Merkle tree root and the hash of the previous block "to form a blockchain". `principal_name` is `ORIGINAL_LOGIN()`. https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-ledger
- Syntax `WITH (LEDGER = ON (APPEND_ONLY = ON))` and blocking of `UPDATE` and `DELETE`. https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-how-to-append-only-ledger-tables and https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-append-only-ledger-tables
- Limits (no conversion, no deletion, 200 tables per transaction, LRS not supported for digests). https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits
- Automatic digests every 30 seconds, only when there are transactions. Container `sqldbledgerdigests`. "Azure Confidential Ledger has a stronger integrity guarantee for customers who might be concerned about privileged administrators access to the digest". Behaviour on restore, failover and geo-replication. https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management
- Verification, `last_verified_block_id`, codes `0` and `1`, `ALLOW_SNAPSHOT_ISOLATION`, scheduling with Elastic Jobs or Azure Automation. "The verification process and the integrity of the database depend on the integrity of the input digests." https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-verification
- Alerts on failed uploads and on a drop in uploads. https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-monitor-digest-uploads
- Immutability policy, `locked` state, `allowProtectedAppendWrites`, the account cannot be deleted with a `locked` policy. https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview
- Confidential Ledger: 8 KiB entry, 2 ledgers per subscription, irreversible deletion. https://learn.microsoft.com/en-us/azure/confidential-ledger/limits
- Elastic Jobs not supported with Entra-only. https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication
- `isLedgerOn` cannot be changed after creation. https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/databases
- GA status of the ledger in Azure SQL Database: NOT VERIFIED as a literal sentence. The pages show "Applies to" with no preview marker. https://learn.microsoft.com/en-us/azure/azure-sql/database/ledger-landing

## Status

Proposed. Human decisions still to be taken before any lock: `isLedgerOn`, retention, pseudonymisation and destination of the digests.
