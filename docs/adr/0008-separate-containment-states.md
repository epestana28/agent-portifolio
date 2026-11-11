# ADR 0008: Separate containment states

Status: proposed. Source verification date: 2026-11-11.

## Context

"Suspending an agent" mixes three things: the product decision, the actions taken outside (Entra, grants, runtime) and the proof that those actions had effect. A single state hides the differences. An agent marked as "suspended" may still have a valid token, a direct grant or a managed identity with a cache.

The Microsoft documentation shows several cases where the administrative action and the real effect diverge. The cache of Exchange RBAC for Applications is bypassed by the test cmdlet. The SharePoint download URL stays valid after revocation. A 200 does not prove that the operation had the intended effect.

Actual state: the code has `AgentState`, `ContainmentState` and `StepStatus` separate. The aggregation is in the local slice and is tested with simulated adapters. No probe has run against real services.

## Decision

Three separate state machines.

| Machine | Values |
|---|---|
| `AgentState` | Draft, PendingReview, Active, Suspended, Retired |
| `ContainmentState` | Requested, LocalBlocked, ExternalPending, VerifiedScoped, Partial, Failed, Unknown |
| `StepStatus` | Requested, Executed, Verified, Failed, Unknown |

Aggregation rules for `ContainmentState`:

1. `VerifiedScoped` only exists if all steps are `Verified` by probe, with the scope of the verification explicit.
2. Any `Unknown` step makes the whole `Unknown`.
3. A mix of verified and failed steps gives `Partial`.
4. All steps failed give `Failed`.
5. `Suspended` never becomes `VerifiedScoped` by itself. They are different machines.

Other rules:

- The suspension is a single transaction: `Suspended`, epoch incremented, `ContainmentOperation` in `LocalBlocked`, `InFlightActionIds`, outbox and audit.
- Administrative confirmation (the call returned success) is distinct from probe confirmation (the effect was observed). Only the second leads to `Verified`.
- Each step and each standing limitation (`StandingLimitation`) appear in the report. For example, an already issued token that stays valid, or a managed identity with no disable.
- A probe that cannot conclude gives `Unknown`, not `Failed` or `Verified`.
- Reactivation is its own flow: requested by one PolicyAdmin, approved by another, with grants re-verified, a new epoch and a new `policy_version`.

## Consequences

- The containment report honestly shows what is known and what is not known. `VerifiedScoped` is rare and has value.
- A probe has to be designed per step and per identity type. Some do not exist in the documentation (for example, a token issuance probe after disable). They remain as gaps and block milestone D.
- The operator often sees `Partial` or `Unknown`. The interface and the runbook have to explain what to do.
- `Unknown` states stay open until a later probe resolves them. Reprocessing and deadlines are needed.
- More states mean more transition and concurrency tests (simultaneous suspension and reactivation).

## Rejected alternatives

- A single `Suspended` state with a "contained" boolean: hides `Partial` and `Unknown`.
- Marking `VerifiedScoped` when the API call returns success: confuses action with effect. The documentation shows cases where they differ.
- Treating `Unknown` as `Failed`: leads to repeating external actions that may already have been done. Treating it as `Verified` would be worse.
- Reactivating automatically when containment fails: contradicts the rule of human approval by a second PolicyAdmin.
- Waiting for external confirmation before blocking locally: the local block is immediate and does not depend on any external service.

## Documentary evidence

All verified on 2026-11-11.

- The `Test-ServicePrincipalAuthorization` cmdlet bypasses the RBAC for Applications cache, and cache propagation takes from 30 minutes to 2 hours. Read by excerpt. https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
- The SharePoint pre-authenticated download URL stays valid after access is withdrawn. https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0
- "A 200 OK isn't proof of ingestion". Sentence from a page about event ingestion, used here by analogy. The application to this case is an inference. https://learn.microsoft.com/en-us/azure/azure-monitor/logs/logs-ingestion-api-overview
- Tokens already issued after disabling an agent identity: NOT VERIFIED. https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
- Managed identity with no documented disable and with a token cache of about 24 hours. https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq
- The design of the three machines is a project decision and does not come from a Microsoft page.

## Status

Proposed. Closure depends on real probes in phases C and D.
