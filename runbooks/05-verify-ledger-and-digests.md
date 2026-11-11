# 05. Verify the ledger and the digests

Status of this runbook: written from the official SQL Ledger documentation, with no subscription and no database in Azure. Never executed in Azure. The T-SQL commands come from the documentation; their real execution is left for Phase C.

## Objective

Prove, with evidence, that the ledger has not been tampered with, and that the digests that protect it exist, are recent and are held in storage that whoever administers the database cannot change.

What the ledger gives: evidence of tampering after running the verification against digests stored outside the database. It does not prevent tampering, does not prevent a false initial entry, does not guarantee availability and does not recover data. Without reliable external digests and without periodic verification, the guarantee does not exist in practice: the verification and the integrity of the database depend on the integrity of the input digests.

## Who can run it

Role `AgentPortifolio.Auditor` in the application and, in SQL, an Entra principal with the `VIEW LEDGER CONTENT` permission (verification) and, if a manual digest has to be generated, `GENERATE LEDGER DIGEST`. With Entra-only authentication, there is no fallback SQL account.

Whoever verifies must not be whoever writes to the ledger or whoever administers the digest account. Separation of privileges between SQL, digest destination and verification is a design requirement.

## Prerequisites

1. Automatic digest upload configured and working (resource `ledgerDigestUploads` in Bicep, enabled only after the SQL server has the `Storage Blob Data Contributor` role on the digest account).
2. Immutability policy on the `sqldbledgerdigests` container, with `allowProtectedAppendWrites`, and (in production) locked. In a test environment it can be unlocked. See the Bicep README.
3. `ALLOW_SNAPSHOT_ISOLATION` enabled on the database. The documentation requires it before running the verification procedures.
4. Access to SQL through Entra, from a client that supports it.
5. A suitable execution window: verification can consume many resources on large databases. Agree it outside peak hours.
6. With Entra-only authentication, Elastic Jobs is not supported. Scheduled periodic verification uses Azure Automation (stated in the documentation) or a job of the organisation's own, still to be defined in Phase C.

## Steps

1. Confirm the digest configuration.
   - The container must be named `sqldbledgerdigests`, with a path of the type `ServerName/DatabaseName/CreationTime`, in the expected storage account.
   - Check that the destination has not been changed to storage without a locked immutability policy. The documentation warns that a privileged user can change the digest location to unprotected storage. Review the configuration and the account policy in the portal or with the approved command.
   - The storage account cannot be LRS (documented limit of automatic upload).
2. Read the known digest locations of the database:

   ```sql
   SELECT * FROM sys.database_ledger_digest_locations;
   ```

3. Measure the age of the last digest.
   - Digests are generated in a 30-second interval and uploaded to the destination, but only if there are transactions in that interval. The absence of a digest during a period with no transactions is not a failure.
   - Get the time of the last commit in the ledger:

     ```sql
     SELECT MAX(commit_time) AS last_commit_time
     FROM sys.database_ledger_transactions;
     ```

   - Get the time of the last digest in storage: the most recent blob in the container (the JSON format of the digest includes `digest_time` and `last_transaction_commit_time`). Read the container with the approved method. The exact listing command was not verified in this research: confirm in the current documentation.
   - Compare. Age of the last digest = now minus `digest_time`. If there were commits after the `last_transaction_commit_time` of the last digest and the interval is greater than the limit approved by the organisation, it is an export failure. The limit is not fixed in this runbook.
4. Run the verification against the digest storage, with the script from the documentation:

   ```sql
   DECLARE @digest_locations NVARCHAR(MAX) = (SELECT * FROM sys.database_ledger_digest_locations FOR JSON AUTO, INCLUDE_NULL_VALUES);
   SELECT @digest_locations AS digest_locations;
   BEGIN TRY
       EXEC sys.sp_verify_database_ledger_from_digest_storage @digest_locations;
       SELECT 'Ledger verification succeeded.' AS Result;
   END TRY
   BEGIN CATCH
       THROW;
   END CATCH
   ```

5. Interpret. The return code of `sp_verify_database_ledger` and of `sp_verify_database_ledger_from_digest_storage` is `0` on success and `1` on failure. The procedure returns a row with the column `last_verified_block_id`. Record that value.
6. To verify a single table, the procedure accepts the optional parameter `@table_name`. Useful for the audit events table.
7. Optional, for an immediate checkpoint: generate a manual digest (`EXECUTE sp_generate_database_ledger_digest;`, requires `GENERATE LEDGER DIGEST`) and verify again. A manual digest closes the current block.
8. Record the result and the age of the last digest. Schedule the next verification. The frequency is decided by the customer and approved by the organisation, and this runbook does not fix it.

### If the verification fails, or the digest age is excessive

Stop and do not "fix" anything in the database. A verification failure is a security incident until proven otherwise.

1. Declare the incident, open a ticket and notify the security officer.
2. Preserve the state: do not restore, do not delete blobs, do not change permissions before collecting evidence. Keep the verification result, the JSON of the digest locations and the list of blobs in the container.
3. Distinguish the probable causes, without concluding before having proof:
   - Export failure: permissions of the server identity on the account revoked, or a network configuration that makes the account inaccessible. The documentation mentions both as causes of a failed upload. Also check whether someone disabled automatic upload.
   - Recent restore: the database gained a new "incarnation", and the verification uses the digests of all incarnations. See runbook 06.
   - Tampering: the calculated hash does not match the digest. The documentation has a dedicated page on recovery after tampering, which must be read in full before acting. Page: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-how-to-recover-after-tampering
4. Treat the audit of the affected interval as unreliable. Use independent evidence: Entra sign-in and audit logs, APIM logs, SharePoint and Microsoft 365 logs, and Service Bus.
5. Suspending the agents whose actions fall in the affected interval is a decision for a `PolicyAdmin`, case by case (runbook 01). It is not automatic.
6. Fix the cause (for example restore the server write role on the account) and verify again. Record everything.

## How to verify

1. The verification returned code `0` and the success message, with `last_verified_block_id` recorded.
2. The age of the last digest is within the approved limit, bearing in mind that there is no digest without transactions.
3. The immutability policy of the container exists, protects append writes, and is locked in production.
4. The digest destination is the expected one, and has not been swapped.
5. Alerts are configured for upload failures and for a drop in the number of successful uploads (the documentation recommends both). The exact metric names were not verified: confirm in the current documentation.
6. For test BT-16 (tampering with evidence is detected in the supported scenarios), repeat in a test environment: change data outside the API and confirm that the verification fails. Only in a test environment, never in production.

## Evidence to record

- UTC date and time, who executed, database and server.
- Verification result (success or failure), `last_verified_block_id` and return code.
- JSON of the digest locations used.
- Age of the last digest, `last_commit_time` and `digest_time` compared.
- State of the immutability policy (period, locked or not) and of the destination.
- On failure: everything that was preserved, probable cause, decisions and ticket.

The digests contain the database name, block identifier, hash, last commit time and digest time, and the blob path contains the server and database name. They do not contain tokens, secrets or content. Even so, do not paste the contents of the blobs into tickets.

## Known limits

- The ledger offers forward integrity: evidence of tampering in data that is in the ledger. It does not prove that the original entry was true.
- Whoever controls the machine or the files can change data. The ledger does not prevent it and guarantees that it will be detected when verifying against reliable digests.
- Verification is resource-intensive on large databases. The documentation does not fix the time.
- There is no supported way to delete rows in append-only ledger tables. Personal data must not enter the ledger or the immutable storage.
- Azure Confidential Ledger offers a stronger guarantee against privileged administrators, with its own authorisation, but has limits (for example 2 ledgers per subscription by default) and replicates to paired regions with no option to disable it. It stays as an evaluated option, and is not in the Bicep.
- The maximum transaction size in bytes is not documented. The documentation only gives the limit of 200 ledger tables per transaction.
- The exact `GRANT` syntax of the `VIEW LEDGER CONTENT` and `GENERATE LEDGER DIGEST` permissions was not read: confirm in the current documentation.
- The literal sentence on general availability of the ledger in Azure SQL Database was not found. All ledger pages apply to Azure SQL Database with no preview notice.

## Official documentation (verified on 2026-11-11)

- Digest management: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management
- Configure automatic digests: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-how-to-enable-automatic-digest-storage
- Database verification: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-verification
- `sp_verify_database_ledger_from_digest_storage`: https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sys-sp-verify-database-ledger-from-digest-storage-transact-sql
- Monitor digest uploads: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-monitor-digest-uploads
- Recover after tampering: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-how-to-recover-after-tampering
- Ledger limits: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits
- Immutable Blob, overview: https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview
