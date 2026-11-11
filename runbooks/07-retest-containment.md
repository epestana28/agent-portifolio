# 07. Repeating the containment tests

Status of this runbook: the local tests correspond to Phase B (vertical slice in .NET 8, without real credentials). The Microsoft world is simulated. Phases C, D and E have not been executed.

## Objective

Allow an independent person to repeat the blocking tests from this document, to know what each one proves, and to know which ones cannot be proved locally.

A local green result does not prove real containment. An API with a 200 response, a green dashboard or a demonstration with simulated adapters do not prove containment.

## Who can run this

Any engineer with access to the repository and to an environment with the .NET 8 SDK and Docker. For the repetition to be independent, preferably someone who did not write the tests.

In phases C and D, the real tests require the Microsoft roles and the approval described in runbooks 01 to 04.

## Prerequisites

1. Repository cloned, at the commit to be validated. Note the commit hash.
2. .NET SDK 8 (`global.json` pins version `8.0.100`, with `rollForward` set to `latestFeature`).
3. Docker, for a test SQL Server 2022 (the 5 `SqlLedgerTests` tests only run with it). Without it, those 5 are skipped and the result is 73 green tests plus 5 skipped. Docker Compose is only needed for step 2.
4. No real credentials. The local tests do not need them and must not use them. If any is requested, stop.
5. Do not run against a real tenant or subscription without reading runbooks 01 to 04 and without approval.

## Steps

1. Build and run all the tests without SQL Server:

   ```bash
   dotnet test
   ```

   The solution has tests in `tests/Unit`, `tests/Integration`, `tests/Security` and `tests/FailureInjection`. Only the `SqlLedgerTests` class (in `tests/Integration`) needs SQL Server and is skipped with the message `AP_TEST_SQL is not set` when the variable does not exist. The others run in memory.
2. Start a test SQL Server 2022 and point the `AP_TEST_SQL` variable at the server (connection string to the server, without a database):

   ```bash
   docker run -d --name ap-sql-test -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="<local password>" -p 127.0.0.1:1433:1433 mcr.microsoft.com/mssql/server:2022-latest
   export AP_TEST_SQL="Server=localhost,1433;User Id=sa;Password=<local password>;Encrypt=True;TrustServerCertificate=True"
   ```

   Each run creates its own database (`ap_test_<guid>`) and deletes it at the end. The `sql` service in the `docker-compose.yml` at the repository root does not publish the port on the host, so it is not suitable for this variable without changing the file. `docker compose up` is for the full local stack (SQL Server, `control`, `executor` and `worker`), which needs `AP_SQL_PASSWORD`, `AP_DEV_SIGNING_KEY` and `AP_TENANT_ID` in `.env` (`scripts/quickstart.sh` creates one with random values) and exercises the local slice over HTTP. It is not needed for `dotnet test`.
3. Run `dotnet test` again, now with `AP_TEST_SQL` set. The result measured on 2026-11-11 was 78 tests, 78 passed, 0 failed.
4. Optional, for a specific test: filter by the method prefix. Each test that covers a BT is named `BTnn_...`, for example:

   ```bash
   dotnet test --filter "DisplayName~BT16"
   ```

   A test without the `BTnn_` prefix does not count towards the list below. The full matrix, with the method names, is in `docs/test-plan.md`.
5. Repeat the complete run a second time, to detect flaky tests. A test that fails in one run and passes in another is a defect and not a success.
6. Stop the environment (`docker rm -f ap-sql-test`, and `docker compose down -v` if you brought the stack up) and remove the test volumes. Do not leave test data in shared environments.
7. Fill in the results table below.

## The 20 blocking tests

Order and identifiers from the specification (section 15). The "Where it is proved" column is the current state. "Local" means proved against the simulated Microsoft world. "Local, real SQL" means that, in addition, there is a test against a real SQL Server 2022 (only with `AP_TEST_SQL`). "Simulation only" means that the claim is about the real behaviour of a Microsoft service and only the simulation exists, which does not count as proof. "C/D" means that it is only proved in phases C or D, with real resources. For these, what exists locally is, at most, a simulated approximation, which does not count as proof.

| ID | What it proves | Where it is proved | Notes |
| --- | --- | --- | --- |
| BT-01 | An authorised resource works and an unauthorised resource fails. | Local, simulated world | Proof with real Selected resources belongs to Phase C. |
| BT-02 | A forged caller and tenant fail (identity from the token, never from the body). | Local | Validation of real Entra tokens is verified in Phase C. |
| BT-03 | Suspension invalidates a previous capability that is still within its validity (epoch). | Local | |
| BT-04 | In the suspension and execution race, in-flight actions are documented exactly. | Local | The real window against Graph does not exist locally. |
| BT-05 | The gateway and the backend cannot be bypassed. | C/D | Requires the real APIM and executor with network and identities. Locally there is only an approximation. |
| BT-06 | The agent has no alternative credential for Graph. | Simulation only | The test only looks for credentials in the responses of the simulated executor. It requires an inventory of the real accesses of the agent in the tenant. |
| BT-07 | Withdrawing one capability does not improperly change others. | Local | |
| BT-08 | With SQL unavailable, new protected actions are denied (`state_unavailable`). | Local, real SQL | There is also a test against an unreachable server. Real Azure SQL unavailability belongs to Phase C. |
| BT-09 | With the bus unavailable, the persistent block remains effective (the outbox is in the database). | Local | There is no Service Bus: the test proves the block without a worker. |
| BT-10 | Repeating a message does not duplicate the effect. | Local, real SQL | Concurrent requests with the same key against real SQL. Idempotency of the adapters against real services belongs to Phase D. |
| BT-11 | An external timeout gives `Unknown` and reconciliation, not an invented success. | Local, with injected failure | |
| BT-12 | Microsoft throttling produces a visible partial state. | Local, with injected failure | The real behaviour of Graph and SharePoint belongs to Phase D. |
| BT-13 | Deactivating one agent does not deactivate the blueprint siblings. | Simulation only | The real effect in a tenant belongs to Phase D. |
| BT-14 | Test a token issued before deactivation on a test resource and report the real result. | Simulation only | The local test is named `BT14_SIMULATED_...` and does not count as proof. It only makes sense with real Entra and a real resource (C/D). |
| BT-15 | Grant drift is detected and handled. | Local, simulated world | Reconciliation with real grants belongs to Phase C. |
| BT-16 | Alteration of evidence is detected in the scenarios supported by the ledger. | Local, real SQL | The `BT16_` tests prove that the engine rejects `UPDATE`, `DELETE` and `TRUNCATE` and that a tampered digest fails verification. Digests in independent immutable storage and Azure SQL belong to Phase C only. |
| BT-17 | Restore and failover do not reactivate a suspended agent; RPO validated and reconciliation before reopening traffic. | C/D | Locally there is only a restart of the services over the same database, which is neither restore nor failover. Requires a real restore or failover. See runbook 06. |
| BT-18 | There are no secrets or unnecessary sensitive content in the ledger or in the logs. | Local | The test covers the audit, the actions and the containments in memory. It does not cover the host logs or the SQL ledger. Also review the real logs in Phase C. |
| BT-19 | An operator without a role cannot suspend, reactivate or grant capabilities. | Local | The assignment of real roles in Entra belongs to Phase C. |
| BT-20 | Break-glass works with the application unavailable. | C/D | There is no automatic test. It is only proved by rehearsing runbook 04 with the application switched off. |

Summary: BT-05, BT-17 and BT-20 are only proved in phases C or D, and BT-06, BT-13 and BT-14 only exist in simulation. The rest are proved within the scope of the local simulation (BT-08, BT-10 and BT-16 also against a real SQL Server 2022), and the part that touches real Microsoft services is confirmed in phases C and D.

## How to verify

1. All the tests pass in both runs. In the run with `AP_TEST_SQL` there are no skipped tests. In the run without it, the 5 in `SqlLedgerTests` are skipped by design and must be declared as such. Any other skipped test without justification counts as a failure.
2. Each of BT-01 to BT-20 has at least one identified test, or is marked "C/D only" with the explanation. A BT with no test and no mark is a gap.
3. The six tests that are only proved in C/D or only exist in simulation (BT-05, BT-06, BT-13, BT-14, BT-17 and BT-20) do not appear as "proved". If any report presents them as proved on the basis of a local run, the report is wrong.
4. Failure injection produces `Unknown`, `Partial` and `Failed` when it should, and never `VerifiedScoped` by mistake. `Suspended` never moves to `VerifiedScoped` on its own.
5. No real credential was used.

## Evidence to record

- Commit hash, date and time, who ran it, operating system and versions of the .NET SDK and Docker.
- Full result of the run (number of tests, passed, failed, skipped), for both runs.
- Table of the 20 tests filled in with: the test or tests that cover each one, the result, and where it is proved (local or C/D).
- Flaky tests found and what was done.
- Deviations from this runbook.

Test reports must not contain secrets. Review them before sharing.

## Known limits

- The local Microsoft world is a simulation (Entra adapters, grant removal, runtime isolation and old-token probe). It does not prove the real behaviour of Entra, Graph, SharePoint or Exchange.
- There is no real adapter for Entra or for Graph at the time this runbook was written.
- Failure injection covers the failures that someone anticipated. It does not cover those that Microsoft may introduce.
- BT-05, BT-06, BT-13, BT-14, BT-17 and BT-20 cannot be proved locally.
- The MVP definition of done requires a real agent in a test environment, and an independent person repeating the tests from the runbook. This document is only the local part of that requirement.
- The naming convention is the `BTnn_` prefix on the method. A BT may have several tests. The exact names are in `docs/test-plan.md`.
