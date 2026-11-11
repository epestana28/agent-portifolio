# ADR 0006: SharePoint with Selected permissions

Status: proposed. Source verification date: 2026-11-11.

## Context

The first use case is reading and listing documents in SharePoint. Broad application permissions (`Sites.Read.All`, `Files.Read.All`) give access to all sites in the tenant. An identity with one of them bypasses any limit that the product imposes in front.

Actual state: the operation catalogue has only `sharepoint.document.read` and `sharepoint.document.list`. The canonical resource is `sharepoint:site/{siteId}/drive/{driveId}`. Reconciliation suspends the agent when it sees a broad grant. No real Graph was called: the grant source and the document gateway are simulated.

## Decision

1. Use `Sites.Selected` with the `read` role for reading. Writing, and any higher role, is outside the MVP.
2. Consent for the application and the grant on the resource are two separate steps. Without both and a valid token there is no access.
3. The identity that grants (grantor) is different from the executing identity. The grantor does not read documents and the executor does not grant.
4. Forbid `Sites.Read.All`, `Files.Read.All`, `Sites.ReadWrite.All`, `Sites.FullControl.All` and broad equivalents on agent and executor identities. The presence of one suspends the agent at reconciliation.
5. Revocation control points in two layers: remove the grant on the resource (or revoke the consent) and the local block by epoch. Each layer is verified by a probe, not assumed.
6. Each agent has its own `ExecutorObjectId`, so a grant to the executor of one agent does not widen another.
7. The executor only accepts the resource in canonical form. It does not normalise. `$batch` and 302 redirects are not accepted as a way to bypass the operation catalogue.
8. The contradictions in the documentation (see below) are recorded as gaps L-19 to L-21. They are not resolved by assumption. They are resolved in the tenant in phase C.

## Consequences

- The risk scope stays on the granted site or library, instead of the whole tenant.
- Removing grants at list or item level is only documented in beta. In v1.0, the documented removal is at site level, or by revoking the consent. This limits the granularity of revocation.
- The pre-authenticated download URL (302) stays valid after access is withdrawn. The validity is contradictory between sources (minutes against 1 hour). An agent that already has the URL can download until it expires (T-06). The executor must not return that URL to the agent.
- `$batch` accepts up to 20 requests and returns an outer 200 with inner statuses. The executor has to inspect the inside. It is one of the bypass vectors handled in the threat model.
- Throttling with 429 and 503 and `Retry-After`. Load testing is not permitted on SharePoint Online, so the throughput limit is not validated by load.
- The real format of site identifiers is an assumption (P-01).

## Rejected alternatives

- `Sites.Read.All` with filtering in the gateway: the agent or the executor can then read everything, and an error in the gateway exposes the whole tenant.
- Delegated permissions on behalf of the user: they tie access to the user, and the MVP is application mode only (ADR 0002).
- A single executing identity for all agents: a grant to one agent widens all of them.
- Anonymous sharing link or organisation link as the access mechanism: outside the model and hard to revoke.
- Exchange RBAC for Applications for this case: it is for Exchange, not SharePoint.

## Documentary evidence

All verified on 2026-11-11. The contradictions are flagged and have not been softened.

- Selected permissions require consent, a grant on the resource and a token. https://learn.microsoft.com/en-us/graph/permissions-selected-overview
- The `read` role and the site permissions endpoint. https://learn.microsoft.com/en-us/graph/api/site-post-permissions?view=graph-rest-1.0
- Contradiction: the item read page lists `Files.Read.All` and `Sites.Read.All` among the least privileged permissions, while the Selected overview says `Sites.Selected` is enough. https://learn.microsoft.com/en-us/graph/api/driveitem-get?view=graph-rest-1.0
- Contradiction: the grant at list and item level requires `Sites.FullControl.All` on one page and `Sites.ReadWrite.All` on another. https://learn.microsoft.com/en-us/graph/permissions-selected-overview
- Removal of permissions at site level is application-only and is in v1.0. Removal by list or item is only in beta. https://learn.microsoft.com/en-us/graph/api/site-delete-permission?view=graph-rest-1.0
- The 302 response with a pre-authenticated URL, validity "minutes" on one page and "1 hour" on another, with no immediate invalidation. https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0
- `$batch`: up to 20 requests, outer 200 response with inner statuses. https://learn.microsoft.com/en-us/graph/json-batching
- Throttling with `Retry-After`, and load testing not permitted on SharePoint Online. https://learn.microsoft.com/en-us/sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online

## Status

Proposed. Validation of the contradictions and of the effects of revocation is left for phase C.
