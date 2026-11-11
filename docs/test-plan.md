# Test plan

This document links the 20 blocking tests in section 15 of the specification (BT-01 to BT-20) to the tests that exist in `tests/`, says what each one proves and says what is missing for real proof.

Reading rule: everything that runs today runs against `SimulatedMicrosoftEnvironment`. That simulator only proves the control logic of this product (admission, state, epoch, containment, audit). It proves nothing about the behaviour of Entra, Graph, SharePoint, APIM or Azure SQL. When the text says "proven locally", it means "the product logic reacts correctly to what the simulator gives it".

## 1. Measured result

On 2026-11-11, `dotnet test` with `AP_TEST_SQL` set against a `mcr.microsoft.com/mssql/server:2022-latest` container gave 78 tests, 78 passed, 0 failed.

Without `AP_TEST_SQL`, 73 pass and 5 are skipped. The 5 are the tests of the `SqlLedgerTests` class, which state the reason for the skip: `AP_TEST_SQL is not set; SQL Server ledger tests were not run.` A skip is not a pass. A report that shows 73 green tests without saying that 5 were skipped is incomplete.

The number 78 counts each `[Theory]` row as one test. There are 60 test methods.

## 2. How to run

All tests, without SQL Server:

```bash
dotnet test
```

With a real SQL Server, `AP_TEST_SQL` holds the connection string to the server, not to a database:

```bash
export AP_TEST_SQL="Server=localhost,1433;User Id=sa;Password=<your password>;Encrypt=True;TrustServerCertificate=True"
dotnet test
```

A SQL Server 2022 test server, run separately (the `sql` service in `docker-compose.yml` does not publish port 1433 on the host, so it is not suitable for this without changing the file):

```bash
docker run -d --name ap-sql-test -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="<your password>" -p 127.0.0.1:1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

The test creates its own database (`ap_test_<guid>`), applies the schema twice to prove that it is idempotent and drops the database at the end. It does not touch any other database. The account must be able to create databases and use `ENABLE LEDGER`, as `sa` can. To remove the server at the end: `docker rm -f ap-sql-test`.

A single BT, by method prefix:

```bash
dotnet test --filter "DisplayName~BT16"
```

The convention is the prefix `BTnn_` in the method name. Tests that prove the same BT from another angle share the prefix.

The HTTP tests (`HttpBoundaryTests`) use `WebApplicationFactory` with `Auth:Mode=Development` and `Adapters:Mode=Simulation`. They do not run the real hosts in a container.

## 3. Matrix BT-01 to BT-20

Status, one of four values:

- `Proven locally`: the product logic is proven, with `InMemoryWalletStore` and a simulated world.
- `Proven locally against a real SQL Server 2022`: the same, and at least one test runs against the real SQL Server 2022 engine, only with `AP_TEST_SQL`.
- `Simulation only`: the claim is about the real behaviour of a Microsoft service and only the simulation exists. It does not count as proof.
- `To be proven on a tenant (phase C/D)`: there is not enough local proof. It needs a tenant or Azure infrastructure.

| Id | Test (section 15) | Status | Test methods | What is missing for real proof |
|---|---|---|---|---|
| BT-01 | An authorised resource works, an unauthorised one fails | Proven locally | `BT01_Authorised_resource_works_and_unauthorised_resource_fails`, `BT01_Effective_access_is_the_intersection_of_wallet_and_external_grant` | Repeat with the real resources and a real negative resource, on the tenant (phase C). The permission matrix per endpoint is still to be validated. |
| BT-02 | A forged caller and a forged tenant fail | Proven locally | `BT02_Forged_caller_and_forged_tenant_fail`, `BT02_Identity_comes_from_the_token_never_from_the_body` | The tokens are development tokens. It still has to be proven with real Entra tokens and confirmed that `tid` and `oid` have the assumed meaning (P-02, P-19). |
| BT-03 | Suspension invalidates a previous capability that is still inside its validity | Proven locally | `BT03_Suspension_invalidates_a_capability_that_is_still_inside_its_validity`. Complementary: `Capability_from_an_earlier_epoch_is_dead_even_inside_its_validity`, `Agent_reads_through_http_and_is_refused_after_suspension` | Repeat with a token captured before the suspension and reused through the real APIM (TC-03, phase C). |
| BT-04 | A race between suspension and execution documents exactly the actions in flight | Proven locally | `BT04_Race_between_suspension_and_execution_reports_exactly_the_actions_in_flight`, `BT04_Suspension_between_admission_and_revalidation_cancels_before_any_external_effect` | The race is orchestrated with a barrier in the simulator, not with real traffic. The race under load on a real SQL Server is not proven for suspension (only for the action limit, see `Concurrent_admissions_respect_the_limit_and_idempotency_under_real_locking`). |
| BT-05 | The gateway and the backend cannot be bypassed | To be proven on a tenant (phase C/D) | Local approximation only: `BT05_Backend_cannot_be_used_without_a_token_or_with_a_token_for_the_other_audience`, `Generic_proxy_batch_and_arbitrary_urls_are_impossible` | The local test only proves that the executor refuses requests without a token and with the wrong audience, and that there is no generic proxy or batch. It does NOT prove that the gateway cannot be bypassed: there is no APIM, no network and no executor identity. Phase C (TC-17). |
| BT-06 | The agent has no alternative credential for Graph | Simulation only | `BT06_Executor_never_returns_a_credential_to_the_agent` | The test looks for markers (`access_token`, `Bearer`, `Authorization`, `downloadUrl`, `secret`, the `ExecutorObjectId`) in the responses and headers of the simulated executor. It does not prove that the agent runtime has no route or credential to Graph. Phase C and D (TC-20). |
| BT-07 | Removing one capability does not change others improperly | Proven locally | `BT07_Revoking_one_capability_does_not_change_the_others` | Nothing specific to the tenant. |
| BT-08 | SQL unavailable: deny new protected actions | Proven locally against a real SQL Server 2022 | `BT08_Store_unavailable_denies_new_protected_actions`, `BT08_Result_that_cannot_be_recorded_is_not_handed_over_as_success`. Against real SQL: `Unreachable_database_fails_closed` | The two `BT08_` tests run in memory, with `Available = false`. The real SQL one uses an unreachable server (`127.0.0.1,1`). It still has to fail a real Azure SQL (failover, firewall, throttling), in phase C. |
| BT-09 | Bus unavailable: the persisted block stays effective | Proven locally | `BT09_Bus_unavailable_the_persisted_block_stays_effective` | There is no Service Bus. The test proves that, with no worker and no delivery, the local block persists and the report says only `LocalBlocked` with scope `local admission only`. When there is a bus, it has to be repeated with the bus unavailable. |
| BT-10 | A repeated message does not duplicate the effect improperly | Proven locally against a real SQL Server 2022 | `BT10_Repeated_request_with_the_same_key_does_not_repeat_the_effect`, `BT10_Redelivered_containment_message_does_not_repeat_verified_steps`. Against real SQL: `Concurrent_admissions_respect_the_limit_and_idempotency_under_real_locking` (10 concurrent requests with the same key, one single effect) | Repeated delivery by a real Service Bus was not exercised. Idempotency of the effects in Entra and Graph is not guaranteed by this product. |
| BT-11 | An external timeout produces unknown and reconciliation, not an invented success | Proven locally | `BT11_External_timeout_produces_unknown_then_reconciliation_never_invented_success`, `BT11_Timeout_of_the_data_call_is_recorded_as_unknown` | The timeout is injected in the simulator. What the real services do with the request that timed out (whether they applied it or not) is only visible on the tenant (phase D). |
| BT-12 | Microsoft throttling produces a visible partial state | Proven locally | `BT12_Throttling_beyond_the_budget_leaves_a_visible_partial_state`, `BT12_Throttling_inside_the_budget_honours_retry_after_and_then_completes` | `Retry-After` is what the simulator returns. The real limits and headers of Graph are NOT VERIFIED by these tests (phase D). |
| BT-13 | Disabling one agent does not disable siblings of the blueprint | Simulation only | `BT13_Disabling_one_agent_does_not_disable_siblings_of_the_same_blueprint` | It only proves that the simulated adapter acts on one object and that the product does not suspend the sibling. The real effect of disabling an agent identity on siblings and on the associated agent user is NOT VERIFIED and is only visible on the tenant (phase D). |
| BT-14 | Test a previously issued token on a test resource and report the real result | Simulation only | `BT14_SIMULATED_Token_issued_before_suspension_keeps_the_report_from_claiming_verified` | It only proves that the report does not claim `VerifiedScoped` while the probe accepts an earlier token, and that the limitation stays in the report. The real result depends on continuous access evaluation and on issuance on the tenant. Phase D, per resource and per identity type. |
| BT-15 | Grant drift is detected and handled | Proven locally | `BT15_Broad_external_grant_is_detected_and_suspends_the_agent`, `BT15_Direct_grant_on_the_agent_identity_lowers_coverage_and_clean_state_restores_it` | The grants come from the simulator. The real reading of grants (SharePoint Selected and permissions consented in Entra) is missing, phase C and D. The list of broad scope names is not exhaustive. |
| BT-16 | Tampering with evidence is detected in the scenarios supported by the ledger | Proven locally against a real SQL Server 2022 | `BT16_Ledger_table_rejects_update_and_delete`, `BT16_Ledger_verifies_against_its_digest_and_a_wrong_digest_is_detected` | It proves that the engine rejects `UPDATE`, `DELETE` and `TRUNCATE` and that `sys.sp_verify_database_ledger` accepts the generated digest and rejects a tampered digest. The digests are generated in the database itself, they are not in independent immutable storage. Missing: external digests, scheduled verification, real tampering with the data files, Azure SQL. Phase C (TC-19). |
| BT-17 | Restore and failover do not reactivate a suspended agent, with RPO and reconciliation validated | To be proven on a tenant (phase C/D) | Approximation only: `Vertical_slice_runs_on_sql_server_and_BT17_a_restart_does_not_reactivate` | The test creates new store objects and services over the same database ("restart"). It is NOT a restore or a failover, and it does not validate RPO. Missing: restore of a copy from before the suspension, geo failover, reconciliation before reopening traffic. Phase C. |
| BT-18 | No secrets or unnecessary sensitive content in the ledger and in the logs | Proven locally | `BT18_Ledger_holds_no_document_content_no_item_identifier_and_no_token`. Complementary: `Every_decision_leaves_the_minimum_audit_fields` | The test serialises the audit, the actions and the containment operations in memory and looks for content, `itemId`, token and `Bearer`. It does not cover the logs of the hosts. It does not run against the SQL ledger. A test on the real logs and on the database is missing. |
| BT-19 | An operator without the role cannot suspend, reactivate or grant capabilities | Proven locally | `BT19_Operator_without_the_role_cannot_suspend_reactivate_or_grant`, `BT19_Roles_are_enforced_on_the_control_api` | The roles arrive in development tokens. It still has to be proven that Entra issues the `AgentPortifolio.*` roles in real tokens (P-19). |
| BT-20 | Break-glass works with the application unavailable | To be proven on a tenant (phase C/D) | None | There is no test. The procedure is in `runbooks/04-break-glass.md`. It needs a rehearsal with the application stopped, on the tenant, phase D. |

Count: 11 proven locally (BT-01, BT-02, BT-03, BT-04, BT-07, BT-09, BT-11, BT-12, BT-15, BT-18, BT-19), 3 proven locally against a real SQL Server 2022 (BT-08, BT-10, BT-16), 3 in simulation only (BT-06, BT-13, BT-14) and 3 to be proven on a tenant (BT-05, BT-17, BT-20). Total: 20.

No BT is proven on a tenant. None of the tests above exercises a real Microsoft service.

## 4. What the simulation does not prove

So as not to read more into it than what is measured:

- `SimulatedMicrosoftEnvironment` is our own code. It answers what we told it to answer. That the product reacts well to the simulator does not say that Entra, Graph or SharePoint behave that way.
- The four containment adapters (`identity.disable`, `grants.remove`, `runtime.isolate`, `tokens.probe`) are simulated. The verification of each step is a simulated probe.
- Time is a fake clock. Expiry of tokens and capabilities proves the comparison logic, not the real issuance.
- `InMemoryWalletStore` serialises transactions with a semaphore. It does not exercise engine concurrency. Real concurrency is only exercised in `Concurrent_admissions_respect_the_limit_and_idempotency_under_real_locking`.
- Authentication in `Development` mode uses a local symmetric key. `Entra` mode has never been exercised.
- Nothing was tested on Azure SQL. The engine used was SQL Server 2022 in a container. Differences in behaviour (for example in the ledger and in the digests) are NOT VERIFIED.

## 5. Extra tests, without the BT prefix

Those that prove domain and lifecycle rules, which the specification does not number. By file.

`tests/Unit/DomainTests.cs`:

- `Non_canonical_resources_are_rejected_not_normalised` (several `[Theory]` rows)
- `Canonical_resource_round_trips`
- `Operations_outside_the_allowlist_are_rejected` (`[Theory]`)
- `Unknown_missing_or_extra_arguments_are_rejected`
- `Item_identifiers_cannot_carry_paths_or_queries` (`[Theory]`)
- `Arguments_digest_is_stable_and_does_not_contain_the_arguments`
- `Valid_capability_is_allowed`
- `Capability_from_an_earlier_epoch_is_dead_even_inside_its_validity`
- `Expired_revoked_and_missing_capabilities_are_denied_with_distinct_reasons`
- `Capability_of_another_agent_or_tenant_never_matches`
- `Only_active_agents_are_admitted` (`[Theory]`)
- `Limits_and_wallet_expiry_are_enforced`
- `Verified_requires_every_step_verified`

`tests/Integration/LifecycleTests.cs`:

- `Activation_needs_a_second_person`
- `Agent_without_owner_or_with_shared_executor_identity_cannot_be_registered` (refuses an executor equal to the agent identity and an empty owner. It does NOT cover two different agents with the same `ExecutorObjectId`: that case, TC-10, is not implemented)
- `Reactivation_needs_second_person_revalidated_grants_and_kills_old_capabilities`
- `Old_containment_message_never_acts_on_a_reactivated_agent_and_never_reactivates`
- `Suspend_is_idempotent_and_does_not_bump_the_epoch_twice`
- `Capability_must_be_allowlisted_canonical_and_short_lived`
- `Retired_agent_stays_denied`

`tests/Integration/VerticalSliceTests.cs`:

- `Every_decision_leaves_the_minimum_audit_fields`
- `Tenants_are_isolated_for_operators_and_for_callers`
- `Rate_limit_of_the_wallet_is_enforced`

`tests/Security/HttpBoundaryTests.cs`:

- `Agent_reads_through_http_and_is_refused_after_suspension`
- `Operator_of_another_tenant_cannot_see_or_touch_the_agent`
- `Generic_proxy_batch_and_arbitrary_urls_are_impossible` (`[Theory]`)

`tests/FailureInjection/FailureTests.cs`:

- `Repeated_failure_is_bounded_and_reported_as_failed`
- `Suspended_never_becomes_verified_without_external_steps`
- `Message_that_keeps_failing_is_dead_lettered_and_the_operation_marked_failed`

`tests/Integration/SqlLedgerTests.cs` (all need `AP_TEST_SQL`):

- `Vertical_slice_runs_on_sql_server_and_BT17_a_restart_does_not_reactivate`
- `Concurrent_admissions_respect_the_limit_and_idempotency_under_real_locking`
- `Unreachable_database_fails_closed`

## 6. Threat tests (TC) to create

The `TC-nn` entries of `threat-model.md` are tests that the specification does not number. Those that can be covered locally and do not yet exist as a dedicated test:

- TC-04 (agent A asks for the resource of agent B): partly covered by `Capability_of_another_agent_or_tenant_never_matches`, with no dedicated HTTP test.
- TC-08 (an agent of another tenant gets the same response as a non-existent agent): partly covered by `Operator_of_another_tenant_cannot_see_or_touch_the_agent` and `Tenants_are_isolated_for_operators_and_for_callers`. The equality of the responses must be confirmed.
- TC-09 (the same PolicyAdmin does not request and approve): covered by `Reactivation_needs_second_person_revalidated_grants_and_kills_old_capabilities` and `Activation_needs_a_second_person`.
- TC-10 (two agents with the same `ExecutorObjectId`): NOT implemented. There is only a test for an executor equal to the agent identity.
- TC-14 (message from an earlier epoch after reactivation): covered by `Old_containment_message_never_acts_on_a_reactivated_agent_and_never_reactivates`.
- TC-18 (injected failures never produce `VerifiedScoped`): covered by `Repeated_failure_is_bounded_and_reported_as_failed`, `Suspended_never_becomes_verified_without_external_steps` and the BT-11, BT-12 and BT-14 tests.

The phase C and D TCs (TC-02, TC-03, TC-11, TC-17, TC-19, TC-20) cannot be written without a tenant or without Azure infrastructure.

## 7. Next steps for proof

In order of risk, what closes most gaps:

1. Phase C: a test Azure with APIM, Azure SQL and immutable storage for digests. Closes BT-05, BT-17, the full BT-16 and BT-08 against Azure SQL.
2. Phase C and D: a tenant with real adapters. Closes BT-01 and BT-02 with real tokens, and BT-06, BT-11, BT-12, BT-13, BT-14 and BT-15 with real behaviour.
3. Phase D: a break-glass rehearsal with the application stopped. Closes BT-20.
4. A test on the logs of the hosts to complete BT-18.
5. A test for TC-10, when the rule exists.

The real adapters do not exist yet: they need a real test tenant and the phase A gate approved first. Until they exist, this matrix cannot move up from "Simulation only" or "To be proven on a tenant".
