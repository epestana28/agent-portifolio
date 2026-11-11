# AdminUi

This folder has no code. The administration interface is postponed. The interface is the Control API (`src/ControlApi`), used through `curl` as in the runbooks.

## Why it is postponed

A graphical interface is only safe if it shows what the system knows and what it does not know. Today the Microsoft side is simulated and no external containment is real. A green screen on top of a simulation would be a claim that the code cannot support. The decision is not to hide gaps behind screens: first the evidence from phases C and D, then the interface.

No deadline has been set. It is listed in `docs/architecture.md`, section "What does not exist yet".

## What the interface would have to show

Everything comes from the Control API. None of this needs new data.

1. Agent state and containment state, shown separately. `Suspended` is the state of the agent. The containment state (`LocalBlocked`, `ExternalPending`, `Partial`, `Failed`, `Unknown`, `VerifiedScoped`) is a different thing and is never merged with the first.
2. Each containment step with its state (`Requested`, `Executed`, `Verified`, `Failed`, `Unknown`), the `limitation`, the attempts, the time of the last verification and the evidence. `Executed` is not `Verified`.
3. The uncertainties (`uncertainties`), always visible, including the one that says tokens already issued may stay valid until they expire. They must not be hidden in a collapsed panel.
4. The scope (`scope`) of what was verified. `VerifiedScoped` only appears with the scope written next to it, never as "access revoked".
5. The actions in flight at the moment of suspension (`inFlightActionIds`) and the outcome of each one, with the `Unknown` ones highlighted.
6. The coverage of the agent (`GatewayEnforced` or `DirectAccessPossible`) and the findings of the last reconciliation, with the time. Without a recent reconciliation, say that there is no data.
7. The recorded external grants, with `lastVerifiedAt`, and whether the approval of a reactivation is blocked by grants without re-verification.
8. The reactivation request: who requested it, and that the approver has to be a different person.
9. A permanent warning that the adapter mode is `Simulation`, for as long as it is. The `X-AgentPortifolio-Simulation` header of the executor exists for this.
10. The audit per agent, only for the `Auditor` role.

## What the interface could never say

- "Revoked", "contained" or "safe" without a `Verified` step and its scope.
- That a token issued before the suspension has stopped working. The code does not know, and the documentation read does not guarantee it either.
- That a state is good because of missing data. Absence of a reading is "unknown", not "ok".
- That `Suspended` implies external containment.

## Rules for when it is written

- It only calls the Control API. It does not read the database and has no executor credential.
- It does not store tokens or document content.
- It shows the error exactly as the API returns it (HTTP code and the `error` field, or `reasonCode` on the executor), without translating it into a friendlier message that changes the meaning.
- Roles are enforced in the API. The interface hiding a button is not access control.
