# ADR 0007: Fail closed, no positive cache

Status: proposed. Source verification date: 2026-11-11.

## Context

Each admission decides whether an agent can act. If the state cannot be read, there are two choices: deny, or assume that the last known state still holds. Assuming is dangerous. A suspension may have happened seconds ago and a positive cache would keep admitting.

The signals on the Microsoft side do not serve as authority. The validity of an access token is long and documented in a contradictory way. CAE can extend the validity and does not cover everything. Nothing guarantees that an already issued token is refused right after the identity is disabled.

Actual state: the `AuthorizationEngine` is deterministic, with no I/O and no cache. If the repository fails, the `ExecutionService` receives `StoreUnavailableException` and returns `state_unavailable`. This was exercised in memory (`BT08_`) and against a SQL Server 2022 in a container, with an unreachable server (`Unreachable_database_fails_closed`). Never against Azure SQL.

## Decision

1. Each admission reads the authoritative state in SQL inside transaction 1. The execution validates state, epoch and capability again in transaction 2, before marking `Executing`.
2. If the state cannot be read, deny with `state_unavailable`. The last known state is never used.
3. There is no positive cache of authorisation, of agent state or of capability, in any layer of the product.
4. The signal from a token (validity, CAE claims, absence of an Entra error) does not replace the state in SQL. It is only one more necessary condition, never a sufficient one.
5. If APIM cannot reach the executor, or the executor cannot reach Entra to validate the token, the request is denied. There is no degraded mode that admits.
6. The unavailability of SQL, APIM and Entra is handled in three separate threats (T-16, T-17 and T-18).
7. For an external action already started when the state fails, the result is `Unknown`. Neither success nor failure is assumed (ADR 0008).
8. An action that stays in `Executing` beyond the deadline is marked by `SweepStaleActionsAsync` with `stale_execution`. The method exists and has a test, but no host calls it today: there is no scheduling.

## Consequences

- The availability of SQL is the availability of the product. A SQL failure denies everything, including legitimate actions.
- The read load on SQL grows with each request. Size and test under load in a tenant (phase C).
- Operator signalling is needed: alerts for repeated `state_unavailable` and for admission latency.
- Security is preferred over availability. It is a conscious decision that the product owner has to accept (risk R-12).
- An action whose result was not recorded returns `result_not_recorded`. The client has to treat an unknown-result response as real.
- Attempts by suspended agents raise `alert.post_suspension_attempt` and serve as a signal of compromise.

## Rejected alternatives

- Positive cache with a short TTL: it keeps admitting during the TTL after a suspension. There is no safe TTL value when immediate effect is required.
- Fail open with later logging: violates the principle of least privilege and the fail-closed rule of the specification.
- Relying on CAE or token expiry for the cut-off: CAE has a window of up to 28 hours for users and 24 hours for workload identities. It does not cover managed identities, and for agents it is not verified.
- A lagging read replica to ease the load: it reads stale state and breaks immediate revocation.
- Degraded mode with local lists only: local lists are a positive cache by another name.

## Documentary evidence

All verified on 2026-11-11.

- Default validity of the access token: 60 to 90 minutes on one page and 1 hour on another. 90 minutes is used as the worst case. https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes
- CAE: tokens of up to 28 hours for users and 24 hours for workload identities. Only Graph and single-tenant principals of LOB applications with `cp1`. It does not cover managed identities. CAE for agents: NOT VERIFIED. https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation-workload
- "Ensure applications revoke their own session tokens and stop accepting Microsoft Entra access tokens even if they're still valid". https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access
- Managed identity: token cache of about 24 hours, no forced refresh. https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq
- The effect of disabling on already issued tokens of agent identities: NOT VERIFIED. https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
- The fail-closed rule with no positive cache comes from the project specification and not from a Microsoft page.

## Status

Proposed. Acceptance of the availability cost is a human decision (R-12).
