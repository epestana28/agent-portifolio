# 03. Reactivate a suspended agent

Status of this runbook: written from the design and the official documentation, without a tenant. Never executed against real Microsoft services.

## Objective

Reactivate a suspended agent, with enhanced review: one `PolicyAdmin` requests, another `PolicyAdmin` approves, the external grants are reverified after the suspension, and the agent returns with a new epoch and a new `policyVersion`.

Reactivation restores nothing from the past. Capabilities issued before the suspension died with the increment of the `revocationEpoch` and have to be reissued. Old containment messages are never replayed. The containment worker never reactivates an agent.

## Who can run this

Two distinct holders of the role `AgentPortifolio.PolicyAdmin`. The requester cannot approve. Reactivation and privilege increases require enhanced review, by design.

For the steps in Microsoft Entra (step 6), an administrator with the role that the documentation indicates for managing agent identities: Agent ID Administrator, or Cloud Application Administrator. The owners of the agent identity can manage their own agents without those roles.

## Prerequisites

1. Cause of the suspension understood and handled, with a ticket. Do not reactivate "to see if it works".
2. Containment state of the suspension read (runbook 01). No external step left unclear: `Unknown` or `Failed` are resolved first.
3. If the reason was compromise of the agent or of its credential: credentials rotated or revoked, and the security review completed.
4. Reverification of the external grants completed after the time of the suspension (step 3). Grants that predate the suspension do not count.
5. Entra tokens of the two `PolicyAdmin` for the Control API audience, by the approved method. Do not record them.
6. If the identity was disabled in Entra during the containment: know that it stays disabled until someone reactivates it (step 6).

## Steps

1. Review the cause and decide the scope of the return: the same capabilities, fewer capabilities, or new capabilities. Increasing privileges requires the same enhanced review and is recorded.
2. The first `PolicyAdmin` registers the request:

   ```bash
   curl -sS -X POST "$CONTROL_API/agents/$AGENT_ID/reactivation-requests" \
     -H "Authorization: Bearer $TOKEN_ADMIN_1"
   ```

   This request has no body. The code does not read any `reason` field here: the reason and the ticket are recorded in the ticket and in the evidence of this runbook, not in the application. The agent must be `Suspended`, otherwise the response is 409 `invalid_state`. The application records who requested (event `agent.reactivation_requested`).
3. Reverify the external grants of the agent, after the suspension and before the approval:
   - Take an inventory of what the agent and its execution identity have in Microsoft Entra and on the resource: application consents, Selected grants on the site or list, Azure role assignments, and (later phase) assignments in Exchange.
   - Compare with the application record. Any grant that is not registered, or that is broad (`Sites.Read.All`, `Files.Read.All`), is dangerous drift and blocks reactivation until it is removed or formally accepted.
   - Grants on the agent itself count as a direct path and lower the coverage (`DirectAccessPossible`).
   - In the application, the reverification is `POST /agents/{id}/reconcile` (role `PolicyAdmin` or `IncidentOperator`). It compares the observed grants with the registered ones and with the active capabilities, and updates `lastVerifiedAt`. A registered grant that no longer appears gets the result `absent`, and counts as reverified.
   - For Selected, the listing of permissions of a site was not verified: confirm in the current documentation before executing. Page: https://learn.microsoft.com/en-us/graph/permissions-selected-overview
   - For Exchange, `Test-ServicePrincipalAuthorization -Identity <...> -Resource <mailbox>` shows the scope assigned in Exchange, and excludes what is granted separately in Entra. An `InScope False` result does not prove absence of access if there is a permission in Entra. Page: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
4. The second `PolicyAdmin`, who did not make the request, reviews the request, the result of step 3 and the cause, and decides. If the decision is positive:

   ```bash
   curl -sS -X POST "$CONTROL_API/agents/$AGENT_ID/reactivation-approvals" \
     -H "Authorization: Bearer $TOKEN_ADMIN_2"
   ```

   Also without a body. Responses, as the code gives them: the same holder approving their own request receives 403. Approving without a prior request gives 409 `review_not_requested`. If any registered grant has `lastVerifiedAt` earlier than or equal to the time of the suspension, the response is 409 `grants_not_revalidated`. The reverification is done with `POST /agents/{id}/reconcile` or with `POST /agents/{id}/grants`, and today it reads the simulator, not the tenant.
5. Read the result. On success the agent becomes `Active` with a new `revocationEpoch` and a new `policyVersion`. The previous capabilities remain invalid.
6. If the identity was disabled in Entra during the containment, reactivate it now, after the approval:
   - Entra admin centre: reactivate the agent identity. The documentation says that reactivating a disabled identity, in any scope, restores access and token issuance. Page: https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
   - Through the API: `PATCH /servicePrincipals/{id}/microsoft.graph.agentIdentity`, permission `AgentIdentity.EnableDisable.All`, in Graph v1.0. The body (`accountEnabled` true) is inference: the reference page does not show an example. Confirm in the current documentation before executing. Page: https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0
   - If the agent has an associated user account (agent user), reactivate that account separately: the policy or the disabling of one is not documented as covering the other. Page: https://learn.microsoft.com/en-us/graph/api/agentuser-update?view=graph-rest-1.0
   - Do not reactivate the blueprint or the main blueprint without a distinct scope, impact analysis and approval: it affects all child identities.
   - If the agent is blocked in the Microsoft 365 admin centre (Agent 365), unblock it too. Whether that block and the Entra one are synchronised: not verified.
   - Managed identities and service principals: if grants were removed, restore only the approved ones, through the steps of runbook 04 in reverse and by an authorised person. If `accountEnabled` of a service principal was changed, page: https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/disable-user-sign-in-portal
7. Reissue the approved capabilities through `POST /agents/{id}/capabilities` (role `PolicyAdmin`, body `action`, `resource`, `expiresAt`). The resource must be in canonical form, the action must be in the closed list, and the validity must be in the future and 90 days at most (`ControlService.MaxCapabilityLifetime`). Each new capability gets the new epoch.
8. Execute the regression tests in the "How to verify" section before handing the agent back to the owner.

## How to verify

1. The agent state is `Active`, with `revocationEpoch` and `policyVersion` higher than before the suspension.
2. An action covered by a reissued capability, on the authorised resource, works (equivalent to `BT01_`).
3. An action on an unauthorised resource fails (`BT01_`, negative part).
4. A capability from the previous epoch, if someone has kept it, is refused with `capability_stale_epoch` (`BT03_`).
5. No old containment message was re-executed: the audit trail shows `containment.superseded` for those of a different epoch, and no automatic reactivation (`BT10_` and, only as an approximation by restart, `BT17_`; the real restore and failover can only be proven in phases C and D).
6. The grant inventory from step 3 is equal to the registered one again, repeated after the reactivation.
7. Watch the agent for an agreed period: direct access alerts, drift or broad grant.

## Evidence to record

- The two `PolicyAdmin` (who requested, who approved), UTC times, ticket and cause.
- Result of the grant reverification (inventory, comparison, differences found and what was done).
- Request and approval, with the identifiers of the audit events `agent.reactivation_requested` and `agent.reactivated`.
- New `revocationEpoch` and new `policyVersion`.
- Reissued capabilities (identifier, action, canonical resource, validity).
- Result of the regression tests.
- External actions performed in Entra or on the resource, by whom and when.

## Known limits

- Reactivating an identity in Entra restores token issuance, but propagation is not immediate on every resource. For reactivation after disabling users, the CAE documentation indicates typical delays (about 15 minutes in SharePoint Online and Teams, and 35 to 40 minutes in Exchange Online). For agents: not verified.
- Grant reverification only sees what the application knows how to query. Alternative paths (API keys, shared secrets, local access to the resource) have to be inventoried by hand.
- Approval by two people does not replace investigating the cause.
- The previous containment was never "reverted" by the application: what was changed outside it (identity, grants, runtime) has to be restored by hand and is recorded.
- Regression proof is only real with real resources (phases C and D).
