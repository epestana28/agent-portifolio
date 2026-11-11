# 01. Suspend an agent

Status of this runbook: written from the design and the official documentation, without a tenant. In phases A and B the external containment steps are simulated. The steps that touch real Microsoft services have never been executed.

## Objective

Immediately block new actions by an agent through the Control API and follow the external containment through to the end, with each step classified as requested, executed, verified, failed or unknown.

Suspension does not prove that the agent no longer has access. It proves that the application no longer admits new actions from it. External access is considered contained only when the external steps are verified by probe.

## Who can run this

Role `AgentPortifolio.IncidentOperator` or `AgentPortifolio.PolicyAdmin`. The code accepts both (`ControlService.SuspendAsync`). An operator without one of these roles receives 403 and cannot suspend (test `BT19_`).

## Prerequisites

1. Internal identifier of the agent (`{id}`) and tenant. A UUID is not authorisation: confirm with the agent owner that it is the right agent.
2. Reason and, if one exists, ticket number.
3. Entra token for the Control API audience (`Auth__Audience` of the control-api), obtained by the method approved in the organisation. Never paste the token into tickets, messages or notes.
4. Base URL of the Control API (`$CONTROL_API`).
5. If the Control API is unavailable, do not insist: go to runbook 04 (break-glass).

## Steps

1. Confirm the agent and the reason with a second person whenever time allows. In an active incident, suspend first and confirm afterwards, because suspension is reversible through runbook 03.
2. Send the suspension request:

   ```bash
   curl -sS -X POST "$CONTROL_API/agents/$AGENT_ID/suspend" \
     -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"reason":"SHORT TEXT OF THE REASON"}'
   ```

   The body is `{"reason":"..."}` and the reason is mandatory: an empty reason gives 400 (`invalid_request`). An unknown field in the body also gives 400. If the agent is already suspended, the request returns the open operation for the current epoch and does not raise the epoch again. If the agent is in a state other than `Active`, the response is 409 `not_suspendable`.
3. Read the HTTP code and the body. A success response means that the local block has been persisted: a single transaction suspended the agent, incremented the `revocationEpoch`, created the containment operation in `LocalBlocked`, listed the in-flight actions and wrote the outbox and the audit trail.
4. If the response was lost (timeout, network error), do not repeat blindly and do not assume anything. Go to the next step: the existence of a containment operation for the agent shows that the suspension was persisted. If the read also fails, treat the application as unavailable and go to runbook 04.
5. Read the containments of the agent:

   ```bash
   curl -sS "$CONTROL_API/agents/$AGENT_ID/containments" \
     -H "Authorization: Bearer $TOKEN"
   ```

6. Interpret the overall state of the most recent operation with the table below. Repeat the read at short intervals until the state stops changing or until you decide to escalate. There is no assumed universal deadline: the time depends on the identity and on the resource.
7. Interpret each step (table below). Read the `limitation` of each one: it is the text that says what the step does not cover.
8. Read the list of in-flight actions (`InFlightActionIds`). These are the actions admitted or running at the moment of suspension. They fall outside the blocking guarantee and are handled separately (section Known limits).
9. If any external step ends in `Failed` or `Unknown`, or the overall state does not reach `VerifiedScoped`, do not repeat the suspension request. Decide with the owner: wait for the worker to retry, or execute the missing step by hand through runbook 04, for that step only.
10. Record the decision and inform the owner of the agent. To reactivate, follow runbook 03. The worker never reactivates an agent.

### Overall containment states

| State | Meaning | What to do |
| --- | --- | --- |
| `Requested` | It exists in the type, but the code creates the operation directly in `LocalBlocked`, in the same transaction as the suspension. This state is not expected to be seen. | If it appears, treat it as a failure and use runbook 04. |
| `LocalBlocked` | Local block persisted. New actions by the agent are denied by the application. No external step is confirmed. | Safe state for the application, not for the resource. Follow the external steps. |
| `ExternalPending` | There are external steps in progress. | Follow up. Do not declare contained. |
| `VerifiedScoped` | All steps were verified by probe, for the explicit scope of each one. | Contained within the listed scope, and only there. Never say "universal access revoked". |
| `Partial` | A mix of verified steps and unverified or failed steps. | List the missing steps. Handle by hand (runbook 04) those that require it. |
| `Failed` | All external steps failed. | Perform the external containment by hand (runbook 04) and open an incident. |
| `Unknown` | At least one step ended without confirmation. | Any `Unknown` step makes the overall state `Unknown`. Do not treat as a success. Verify by hand. |

`Suspended` never becomes `VerifiedScoped` on its own. The agent state and the containment state are separate on purpose.

### States per step

| Step state | Meaning | Caution |
| --- | --- | --- |
| `Requested` | The step was requested and has not run yet. | No confirmed effect. |
| `Executed` | The Microsoft service accepted the action (administrative confirmation). | It is not proof of effect. The probe is missing. |
| `Verified` | A probe confirmed the effect, for the stated scope. | The scope is in the `limitation`. Do not generalise. |
| `Failed` | The step failed. | Read the evidence. Throttling (429 or 503) after the retries are exhausted may appear here or as a visible partial state. |
| `Unknown` | Timeout or response without confirmation. | Reconcile. Do not assume success or failure. |

The JSON fields are those of the model (`src/Core/Domain/Model.cs`), in camelCase and with the states as text: `state`, `scope`, `epochAfter`, `steps` (`name`, `resource`, `status`, `limitation`, `attempts`, `startedAt`, `lastCheckedAt`, `evidence`), `inFlightActionIds` and `uncertainties`. The step names today are `identity.disable`, `grants.remove`, `runtime.isolate` and `tokens.probe`, all simulated. The `identity.disable` step does not apply to managed identities.

## How to verify

1. The agent state is `Suspended` and the containment operation exists, with the `revocationEpoch` incremented.
2. An attempted action by the agent is refused with `agent_suspended` (or `capability_stale_epoch` for capabilities that predate the suspension). In a test environment, make the attempt with a rehearsal agent. In production, do not provoke actions by the real agent: read the audit trail (event `action.denied` with the reason, and `alert.post_suspension_attempt` if the agent tried).
3. Each external step shows the state `Verified` and the scope. Anything below that is a gap, not a success.
4. The list of in-flight actions was read and each action has a known outcome (`Succeeded`, `Failed`, `Unknown`). The `Unknown` ones are recorded for reconciliation.
5. Independent proof on the Microsoft side, when the external containment is real: the Entra sign-in logs for the agent identity (the documentation shows a query in Graph beta against `auditLogs/signIns`, see the management page) and the refusal of a test request to the resource. Confirm in the current documentation before executing.

## Evidence to record

- Agent identifier, tenant, reason, ticket, who executed and at what time (UTC).
- Identifier of the containment operation, value of `revocationEpoch` after the suspension.
- Overall state and state of each step, with the `limitation` and the time of the last verification, on each read.
- List of in-flight actions and the outcome of each one.
- Relevant audit events (suspension, steps, post-suspension refusals).
- Decisions taken (wait, handle by hand) and who approved them.

Do not record tokens, secrets, document content or prompts.

## Known limits

- Actions already admitted and in progress are not interrupted by the suspension. Revalidation immediately before execution catches those that have not started yet. An action already sent to Microsoft Graph may complete. It is recorded as `Unknown` if there is no confirmation. There is no universal rollback.
- Disabling an identity or blocking issuance does not invalidate access tokens that have already been issued. The default lifetime of an access token is 60 to 90 minutes according to one page and 1 hour according to another. For managed identities the token cache is around 24 hours and a refresh cannot be forced. For tokens of specific agents, the documentation does not say what happens.
- CAE is not a universal guarantee. The CAE documentation covers users and, for workload identities, only requests to Microsoft Graph and single-tenant service principals, and it excludes managed identities. Whether an agent identity is covered: not verified.
- A pre-authenticated download URL that has already been issued may remain valid for up to one hour (documented worst case).
- In Exchange, the revocation of an application role assignment takes between 30 minutes and 2 hours to take effect. The test cmdlet ignores the cache and is not proof.
- There are two independent switches for agents with Agent 365: Entra and the Microsoft 365 admin centre. Whether blocking in one changes the other: not verified.
- Tests BT-05, BT-06, BT-14, BT-17 and BT-20 can only be proven in phases C and D.

## Official documentation (verified on 2026-11-11)

- Disable agent identities: https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities
- Manage agent identities (administration): https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
- Revoke access (users, tokens): https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access
- CAE: https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation
- CAE for workload identities: https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation-workload
