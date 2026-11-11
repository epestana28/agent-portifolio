# 04. Break-glass: containment with the application unavailable

Status of this runbook: written from the official documentation, without a tenant. Never executed. Until it is rehearsed in phases C and D with the application switched off, test BT-20 is not proven.

## Objective

Contain an agent directly in the Microsoft services, without using the Control API, the database, the queue or any component of this application.

This runbook is independent of the application on purpose. If the Control API, the SQL, the APIM, the Service Bus or the application's own Entra fail, the procedure keeps working. It depends only on Microsoft accounts and roles that exist outside the application.

Order of action, from the most specific to the broadest: disable the agent identity, remove the grant on the resource, stop the runtime. Tenant-scope policies only as a last resort.

## Who can run this

People with Microsoft roles assigned outside this application and approved by the organisation. According to the documentation:

| Action | Role that the documentation indicates |
| --- | --- |
| Disable or reactivate an agent identity | Agent ID Administrator, or Cloud Application Administrator. The owners of the identity can manage their own agents. |
| Disable the sign-in of a service principal | Cloud Application Administrator, Application Administrator, or owner of the service principal. |
| Disable an agent user | Permission `User.EnableDisableAccount.All` with `User.Read.All` (Graph). The administrative role is not stated on the page. |
| Remove a Selected grant on the site | Application with `Sites.FullControl.All`. Delegated is not supported for this operation. |
| Remove an application role assignment in Exchange | Member of the Organization Management role group, and Exchange Administrator in Entra. |
| Stop or isolate the runtime | Azure role on the runtime. Not verified. Confirm in the current documentation. |

Break-glass accounts and sessions must be reachable without depending on this application: do not store the credentials in the application's Key Vault, and do not require the gateway or the control identity.

## Prerequisites

1. Containment decision taken by someone with authority, and a ticket (even if opened afterwards, during an incident).
2. Identifiers of the agent, collected before the incident and stored outside the application: `object_id` of the identity, `appId`, identity type (Entra Agent ID, managed identity or service principal), blueprint, associated user account, resources and grants (with `permissionId` when it is Selected), and runtime. If the database is accessible, the `ExternalGrant` record has this data. If it is not, use the copy kept outside the application.
3. One person with access to this list and another who knows this runbook.
4. An account with the roles in the table above, tested in the latest rehearsals.
5. Know the scope: one agent. Disabling a blueprint or the tenant requires a distinct visible scope, impact analysis and approval.

## Steps

Do the steps in order. Record each action and its time before moving to the next.

### A. Disable the agent identity

1. Identify the identity type.
2. Entra Agent ID (agent identity): in the Entra admin centre, disable the specific agent identity, not the blueprint. The documentation says that this blocks access and token issuance for that agent and that it can be reverted. Pages: https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities and https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
3. Alternative through the API: `PATCH /servicePrincipals/{id}/microsoft.graph.agentIdentity`, permission `AgentIdentity.EnableDisable.All`, Graph v1.0. The body with `accountEnabled` false is inference: no reference page shows the example. Confirm in the current documentation before executing. Page: https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0
4. If the agent has an associated user account (agent user): disable it too, and revoke the sessions. Pages: https://learn.microsoft.com/en-us/graph/api/agentuser-update?view=graph-rest-1.0 and https://learn.microsoft.com/en-us/graph/api/user-revokesigninsessions?view=graph-rest-1.0 (`POST /users/{id}/revokeSignInSessions`, permission `User.RevokeSessions.All`). The application of session revocation to agent users is inference. Cascading disablement of the agent user from the agent identity is not verified: treat it as two steps.
5. Application service principal: `PATCH https://graph.microsoft.com/v1.0/servicePrincipals/{id}` with `{"accountEnabled": false}`, permission `Application.ReadWrite.All`. The documentation says that this prevents token issuance in that tenant. Page: https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/disable-user-sign-in-portal
6. Managed identity: there is no verified procedure for disabling through `accountEnabled`. Do not delete the identity as a first action. Go to steps B and C. Confirm in the current documentation whether there is a disabling mechanism. Page on limits: https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq
7. Agents with Agent 365: if the organisation uses the block in the Microsoft 365 admin centre, use it too. Whether it changes the state in Entra: not verified. Apply both blocks.
8. Do not apply the three tenant-scope Conditional Access policies in this phase, and do not disable a blueprint, without distinct approval. The documentation says that the policies do not prevent the creation of identities, and that the licence requirements contradict each other between pages. It remains a last resort, with impact analysis.

### B. Remove the grant on the resource

1. SharePoint, Selected grant at site level: `DELETE /sites/{sitesId}/permissions/{permissionId}`, Graph v1.0, application only, `Sites.FullControl.All`. Expected response: 204 with no body. Page: https://learn.microsoft.com/graph/api/site-delete-permission?view=graph-rest-1.0
2. List or item: the documentation found shows only the beta version, and the item path seems incomplete. Confirm in the current documentation before executing. Page: https://learn.microsoft.com/graph/api/list-delete-permissions?view=graph-rest-beta
3. If the `permissionId` is not known and the application is down: the permissions listing was not verified. Documented alternative: revoke the application's consent in Entra, which blocks access to any list that had a grant. The administrative role for the consent was not verified. Page: https://learn.microsoft.com/en-us/graph/permissions-selected-overview
4. Warning: removing a higher-level consent (`Sites.*`) does not remove access given by a lower level (`Lists.*`). Review the levels.
5. Exchange (later phase): `Remove-ManagementRoleAssignment` and, if broad Entra permissions exist, remove those too, because grants are additive. Page: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
6. Azure RBAC: remove the role assignments of the identity on the resources. The documentation says that some systems, such as Azure RBAC, keep accepting the token until it expires. Page: https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq

### C. Stop the runtime

1. Stop or isolate the runtime where the agent runs (Azure Container Apps in the pilot). The concrete command was not verified in this research: confirm in the current documentation before executing.
2. The goal is for the agent to stop running, so that it stops using the credential it already has. Inference, to be validated in rehearsal: stopping the process prevents it from continuing to use tokens it holds in memory, which the actions in steps A and B do not do.
3. Do not stop the Control API or the executor in this phase if it is not necessary: the goal is to contain the agent, and the application may be useful again later for reconciling. Do it only by express decision.
4. If the decision is to close the agent's traffic at the gateway, do it in the APIM by the approved method. The command was not verified.

### D. Verify

1. Entra sign-in logs for the agent identity: after the time of disablement, there must be no issuance of new tokens. The documentation shows a query in Graph beta against `auditLogs/signIns` with a filter by agent type. Confirm the exact request on the management page. Page: https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
2. In a test environment: a test request to the resource with the identity of the agent (or with a new token for the same one) must fail.
3. A token issued before the disablement may remain valid until it expires. Measure and record the real interval in rehearsal (test BT-14). Documented worst case for an access token: 60 to 90 minutes by default, managed identity cache of about 24 hours.
4. Record what remained unverified as `unknown`.

### E. Reconcile when the application returns

1. Suspend the agent in the Control API (runbook 01) so that the local state matches the containment done by hand.
2. Record in the audit trail, by an authorised person, what was done outside the application: action, service, time, who. The application does not know what was done without this record.
3. Reconcile the observed grants with the registered ones.
4. Reactivate only through runbook 03.

## Periodic rehearsal

Break-glass only counts if it is rehearsed. The rehearsal is the evidence for test BT-20 and is done only in phases C and D, with test resources.

1. Frequency: defined and approved by the organisation. This runbook does not set a value. Repeat also after any change of roles, tenant or runtime.
2. Who rehearses: a person independent of whoever wrote the runbook, working from the document, without help.
3. How: with a test agent and a test resource, switch off the application (control-api, executor and worker, or the part that the organisation approves) and execute steps A to D. Time each step.
4. What to record: time of each step, roles used, errors, steps that the text did not allow to be followed, and whether the test resource became inaccessible to the agent. Record the time until the effective refusal and the behaviour of previous tokens.
5. After the rehearsal: reactivate the test agent through runbook 03 and correct this runbook with whatever failed.
6. Also verify that the break-glass accounts sign in and have the right roles. A rehearsal that fails for lack of access is a useful result.

## How to verify (summary)

1. The identity of the agent is disabled, or access is removed, according to the type.
2. The grant on the resource was removed and a test request fails.
3. The runtime is stopped.
4. The sign-in logs show no new issuance.
5. What could not be verified was written down as unknown.

## Evidence to record

- Who decided, who executed, ticket, UTC time of each action.
- Identifiers of the identity, of the grant and of the resource affected.
- Request and response of each action, without tokens, secrets or document content. For actions in the admin centre, a note of what was done and the time.
- Result of the verifications and what remained unknown.
- In the rehearsal: times, failures of the text and corrections.

## Known limits

- Disabling the identity and blocking issuance do not guarantee invalidating the access tokens already issued. The Agent ID documentation does not say what happens to tokens already issued to a disabled agent identity.
- CAE is not a universal guarantee. It covers events on users and, for workload identities, only Microsoft Graph, single-tenant service principals and clients that declare support. Managed identities are not supported. Whether an agent identity is covered: not verified.
- Managed identity: token refresh cannot be forced, the cache is around 24 hours, and permission changes may take hours to take effect. Deleting the identity does not invalidate already issued tokens until their original expiry.
- Pre-authenticated download URL: may remain valid for up to one hour after access is removed.
- Exchange: 30 minutes to 2 hours of propagation, and the test cmdlet ignores the cache.
- Conditional Access does not cover managed identities, nor the blueprint creating identities, nor access by API key outside Entra.
- Old Copilot Studio or Foundry agents may exist as service principals without an agent identity. The agent identity steps do not apply to those.
- If a step shows "confirm in the current documentation", do not execute it from this text without confirming.
- What was not requested is not disabled in cascade: disabling an agent identity must not affect sibling identities of the blueprint (BT-13), and this can only be confirmed in a tenant.
- Data already exfiltrated and operations already accepted by external services cannot be undone.

## Official documentation (verified on 2026-11-11)

- Disable agent identities: https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities
- Manage agent identities: https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
- Graph, update agent identity: https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0
- Graph, update agent user: https://learn.microsoft.com/en-us/graph/api/agentuser-update?view=graph-rest-1.0
- Graph, revoke sessions: https://learn.microsoft.com/en-us/graph/api/user-revokesigninsessions?view=graph-rest-1.0
- Disable sign-in of a service principal: https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/disable-user-sign-in-portal
- Managed identities, frequently asked questions: https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq
- Selected permissions: https://learn.microsoft.com/en-us/graph/permissions-selected-overview
- Exchange, RBAC for Applications: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
- Revoke access: https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access
