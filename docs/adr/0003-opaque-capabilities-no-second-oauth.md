# ADR 0003: Opaque capabilities, no second OAuth

Status: proposed. Source verification date: 2026-11-11.

## Context

The product needs to restrict what each agent can do beyond what Entra grants. The decision must be bound to tenant, agent, action, resource, validity and `revocation_epoch`, and it must be possible to withdraw it as soon as the agent is suspended. The specification forbids creating a second JWT or OAuth without need and requires preferring Entra for authentication.

Actual state: the `Capability` in the code is an internal record with an opaque `Id`, bound to tenant, agent, action, canonical resource, validity and `IssuedEpoch`. The agent does not present the capability: the server selects it from the state, based on the authenticated agent, the operation and the resource. This is implemented in the local slice (phase B) and has not been tested against a real APIM or Entra.

## Decision

1. Entra authenticates. APIM validates the token (`validate-azure-ad-token`, with `audiences` and client application mandatory in the policy template). The executor validates the token again and reads the authoritative state.
2. A capability is an internal opaque reference, recorded in SQL. It is not a JWT or a bearer token, and has no value outside the state that holds it.
3. The capability is valid only for the tenant, agent, action, canonical resource, validity and epoch with which it was issued. A capability from an earlier epoch dies with the suspension and has to be reissued.
4. A capability is never a credential for access to data. Data is reachable only through the executor, which holds the isolated Graph credential.
5. The decision uses no language model and no positive cache (ADR 0007).

## Consequences

- There is no offline verification of capabilities: each admission reads the state in SQL. The availability of SQL becomes the availability of the product (ADR 0007).
- Revocation is immediate for the gateway: incrementing the epoch invalidates all earlier capabilities in one transaction, without waiting for any token to expire.
- A stolen agent token brings no capabilities of its own. It stays valid in Entra until it expires, and against Graph directly it is only useful if the agent has a direct grant (T-02).
- Without our own issuer, there are no signing keys or JWKS of our own to manage.
- Trust in APIM is limited: whoever edits APIM policies can bypass it (risk R-06). The executor has to validate the token by itself.

## Rejected alternatives

- A JWT signed by the product as the capability: creates a second issuer, requires keys, and revoking before expiry forces a state lookup, which cancels the advantage of offline verification.
- Handing the executor's Graph token to the agent: violates the trust boundary and opens the way to credential theft (T-02).
- Encoding each capability as a scope or app role in Entra: duplicates authorisation in Entra, has coarse granularity and slow propagation of changes.
- APIM subscription keys as identity: they are not agent authentication and are not tied to any tenant or agent.
- Trusting only APIM to validate the token: Microsoft recommends always validating in the backend.

## Documentary evidence

All verified on 2026-11-11.

- `validate-azure-ad-token`: only `tenant-id` is mandatory, `audiences` and `client-application-ids` are optional in the policy. The requirement to use them is an inference of this project. https://learn.microsoft.com/en-us/azure/api-management/validate-azure-ad-token-policy
- "Always validate tokens in the backend API, even if the gateway validates them" and "Gateway configuration can change and tokens can be replayed". Read by excerpt. https://learn.microsoft.com/en-us/entra/msidweb/advanced/api-gateways#follow-best-practices
- For the resource: "Ensure applications revoke their own session tokens and stop accepting Microsoft Entra access tokens even if they're still valid". https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access
- Whoever edits APIM policies can use the managed identity of the service, and APIM "does not validate which backend the token is sent to". Read by excerpt. https://learn.microsoft.com/en-us/azure/api-management/api-management-howto-use-managed-service-identity#security-considerations-for-managed-identities
- The APIM network mechanisms apply to the gateway to backend leg. The absence of a way to restrict Graph to requests from an APIM is an inference. Read by excerpt. https://learn.microsoft.com/en-us/azure/api-management/secure-api-management#network-security

## Status

Proposed. Validation of the real APIM and of the token claims is left for phase C.
