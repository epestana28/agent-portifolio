# 02. Revoke a capability

Status of this runbook: written from the design and the official documentation, without a tenant. Never executed against real Microsoft services.

## Objective

Remove a single internal capability from an agent (a semantic action on a canonical resource), without changing the other capabilities of the same agent and without suspending the agent.

A capability is an opaque internal reference, bound to tenant, agent, action, canonical resource, validity and epoch. Revoking it does not remove any external grant in Microsoft Entra or on the resource. If the external access also has to end, that is a separate step (step 7).

## Who can run this

Role `AgentPortifolio.IncidentOperator` or `AgentPortifolio.PolicyAdmin`. An operator without one of these roles receives 403 (test `BT19_`).

If there is suspicion that the agent is compromised, do not revoke a capability: suspend the agent (runbook 01).

## Prerequisites

1. Identifier of the agent (`{id}`) and of the capability (`{capabilityId}`). Obtain the list of capabilities of the agent through `GET /agents/{id}`, which returns the agent, the capabilities and the grants. Alternatively, through the audit trail (`capability.granted`, role `Auditor`).
2. Reason and ticket.
3. Entra token for the Control API audience, by the approved method. Do not record it anywhere.
4. Know whether the resource of that capability also has an external grant (`ExternalGrant` record) that must be removed.

## Steps

1. Confirm that the capability is the intended one: action, canonical resource and validity. Revoking a capability with the wrong resource by mistake means the correct one has to be reissued.
2. Send the request:

   ```bash
   curl -sS -X DELETE "$CONTROL_API/agents/$AGENT_ID/capabilities/$CAPABILITY_ID" \
     -H "Authorization: Bearer $TOKEN"
   ```

3. Read the HTTP code. Success (200, with the capability and the `revokedAt` field) means that the capability was revoked in the database and that the `capability.revoked` event was written to the audit trail. The request is idempotent: revoking an already revoked capability returns it without writing another event. An identifier that does not belong to the agent gives 404. If the request fails or gets no response, repeat it only after reading the state of the capability, so as not to mistake a failure for a lost success.
4. Confirm that the agent state has not changed (it is still `Active`) and that the `revocationEpoch` was not incremented: revoking a capability is not suspending. What goes up is the `policyVersion`.
5. In a test environment, try an action covered by that capability with the rehearsal agent. Expected: refusal with `capability_revoked`.
6. Try an action covered by another capability of the same agent. Expected: it still works. This is what test `BT07_Revoking_one_capability_does_not_change_the_others` proves, over the simulated world only.
7. If the external grant must also be removed, handle it separately and with the same care:
   - Selected on SharePoint, at site level: request `DELETE /sites/{sitesId}/permissions/{permissionId}` in Microsoft Graph v1.0, in application mode only, with `Sites.FullControl.All`. Delegated mode is not supported for this operation. Execute through an identity separate from the executor and from the control-api. Page: https://learn.microsoft.com/graph/api/site-delete-permission?view=graph-rest-1.0
   - Selected at list or item level: the documentation found shows only the beta version, and the item path on the page seems incomplete. Confirm in the current documentation before executing.
   - The `permissionId` comes from the `ExternalGrant.ExternalId` record in the database (stored when the grant was registered). The permissions listing endpoint was not verified: confirm in the current documentation.
   - Alternative: revoke the consent in Entra, which blocks access to any list with a previous grant. The administrative role required for the consent was not verified.
   - Exchange (later phase): `Remove-ManagementRoleAssignment`. Page: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
8. After removing the external grant, reconcile with `POST /agents/{id}/reconcile` (role `PolicyAdmin` or `IncidentOperator`): the read of observed and registered grants must match. Today the read is made against the simulator. A broad grant (for example `Sites.Read.All`, `Files.Read.All`) is dangerous drift and suspends the agent.

## How to verify

1. The audit trail has the `capability.revoked` event with the capability identifier and the human initiator.
2. Action under the revoked capability: refused with `capability_revoked`.
3. Action under other capabilities of the agent: still admitted.
4. If the external grant was removed: a test request to the resource fails, and after propagation (which may take time, see Known limits) it keeps failing. Record how long the effective refusal took.

## Evidence to record

- Agent, capability (action and canonical resource), reason, ticket, who executed, UTC time.
- Response of the Control API and identifier of the audit event.
- Result of the tests in steps 5 and 6 (refusal and continuity).
- If applicable: request and response of step 7 (without tokens), and the time until the effective refusal.

## Known limits

- Internal revocation only prevents new admissions. An action already admitted and running is not cancelled. Revalidation before execution catches those that have not started yet.
- Revoking the capability does not remove the grant on the resource. While the external grant exists, there is a path that bypasses the gateway if someone has the executor credential. The coverage of the agent is reduced (`DirectAccessPossible`) until the path is eliminated.
- In Selected, removing the grant from the list removes access to its items, regardless of explicit grants on the items. Removing a higher-level consent does not remove access given by a lower level.
- In Exchange, the removal takes between 30 minutes and 2 hours to take effect, and the test cmdlet ignores the cache.
- A pre-authenticated download URL that has already been issued may keep working for up to one hour after access is removed.
- This operation can only be proven end to end with real resources in phases C and D.

## Official documentation (verified on 2026-11-11)

- Selected permissions: https://learn.microsoft.com/en-us/graph/permissions-selected-overview
- Delete site permission: https://learn.microsoft.com/graph/api/site-delete-permission?view=graph-rest-1.0
- Exchange, RBAC for Applications: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
