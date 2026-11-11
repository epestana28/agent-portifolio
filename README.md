<p align="center"><img src="docs/assets/icon.png" width="128" alt="Agent Portifolio"></p>

# Agent Portifolio

An open prototype of a control and evidence layer for AI agents on Azure and Microsoft 365. It gives each agent a wallet of capabilities, decides every action deterministically, records the decision in an append-only ledger and, during an incident, suspends the agent and states precisely what was contained and what was not.

> **Status: local vertical slice, with the Microsoft side simulated.** The authorisation, blocking, audit and containment orchestration code is real and tested, including against a SQL Server 2022 ledger. There is no real adapter yet for Entra, Graph or SharePoint. Nothing in this repository is evidence of how Microsoft services behave. See [Status by phase](#status-by-phase).

## The problem

An AI agent with access to company data raises three questions that almost nobody can answer with evidence. What exactly can this agent do? What did it do? And if I suspend it now, what really stops working, and when?

The third one is the hardest. Disabling an identity in Entra does not, by itself, invalidate tokens that were already issued. A dashboard that shows "revoked" after a successful administrative call is claiming something it has not verified.

## What it does

- **Capability wallet.** Each agent has an owner, its own identity, a separate executor identity and a short list of capabilities: an action from a closed list, a resource in canonical form, an expiry. There are no wildcards.
- **Deterministic authorisation.** The agent never talks to Graph. It asks the executor for an operation (`POST /actions`), which validates the token, resolves the operation against the closed list and decides in one transaction using the current state. There is no generic proxy, no `$batch` and no arbitrary URL.
- **Fail-closed.** If the state store does not respond, the answer is `state_unavailable` and nothing is executed. Positive decisions are never cached.
- **Ledger audit.** The decision, the intent and the audit event are written in the same transaction. With SQL Server or Azure SQL, the events go to an append-only ledger table of the engine itself. The project does not implement its own hash chain.
- **Immediate suspension.** Suspending is a single transaction: state, revocation epoch increment, containment operation, outbox message and audit. The agent's next request is refused before any external step. An action that was already admitted is revalidated before the external call and cancelled.
- **Containment with honest states.** Each external step (disable identity, remove grants, isolate runtime, probe issued tokens) ends in `Requested`, `Executed`, `Verified`, `Failed` or `Unknown`. The summary is only `VerifiedScoped` if every step was verified, and it always carries the list of uncertainties. A timeout is `Unknown`, never success.
- **Two-person reactivation.** One person requests, another approves, the external grants must be revalidated after the suspension and the old capabilities stay dead (new epoch).
- **Reconciliation.** It compares the grants that exist on the external side with the ones the wallet knows. A broad grant (for example `Sites.Read.All`) suspends the agent. A direct grant on the agent identity lowers the coverage to `DirectAccessPossible`.

An [architecture diagram](docs/architecture-diagram.html) shows the three planes (control, data, worker) and the storage layer.

## Requirements

| What you want to do | What you need |
|---|---|
| Run the demo | Docker, or the .NET 8 SDK |
| Run the local Docker stack (with SQL Server) | Docker with Compose, and `curl` |
| Run the tests | The .NET 8 SDK |
| Run the tests including the SQL Server ledger tests | The .NET 8 SDK and Docker (for `scripts/sql-local-up.sh`) |
| Validate real Entra tokens | An Entra tenant with two app registrations (see below) |
| Contribute | Git, the .NET 8 SDK and Docker |

No Azure or Microsoft 365 credentials are needed for anything except the Entra token validation mode. Step-by-step instructions are in [GETTING_STARTED.md](GETTING_STARTED.md).

## Try it in 2 minutes

No credentials, no tenant, one process:

```bash
docker run --rm ghcr.io/epestana28/agent-portifolio:latest demo
```

Or, with the .NET 8 SDK: `dotnet run --project samples/LocalSlice`.

The demo walks through registration, two-person approval, capability, read, refusals, suspension, containment and reactivation, and prints the audit log.

## Local stack with Docker

It starts SQL Server 2022 (with ledger), the control API, the executor and the containment worker, and runs the whole flow over HTTP:

```bash
git clone https://github.com/epestana28/agent-portifolio.git
cd agent-portifolio
./scripts/quickstart.sh
```

The script creates a `.env` with random values, runs `docker compose up -d`, issues development tokens and shows every response. It needs `docker` (with compose) and `curl`. The control API listens on `127.0.0.1:8081` and the executor on `127.0.0.1:8082`.

The same image contains the three services and two tools. The `AP_SERVICE` variable (or the first argument) selects one: `control`, `executor`, `worker`, `demo`, `devtoken`.

### Use it with your own Microsoft Entra tenant

```bash
docker compose -f docker-compose.yml -f docker-compose.entra.yml up -d
```

With `AP_TENANT_ID` in the `.env`, both services start validating real tokens from your tenant: signature, issuer, audience, expiry and tenant. You need two app registrations with the audiences `api://agent-portifolio-control` and `api://agent-portifolio-executor`, and the app roles `AgentPortifolio.Reader`, `AgentPortifolio.IncidentOperator`, `AgentPortifolio.PolicyAdmin` and `AgentPortifolio.Auditor` on the first one.

What becomes real in this mode: token validation, the wallet, the decisions, the blocking and the ledger. What stays simulated: everything that would be a call to Entra, Graph or SharePoint. The service refuses to start with `Adapters__Mode` other than `Simulation`, so it never pretends to have an integration that does not exist. This mode has not been exercised against a real tenant by the author.

### Limits of the simulation in the Docker stack

Each container has its own simulated environment, in memory. The containment worker does not know the tokens or identities simulated by the other processes, so its steps come back `Verified` trivially and the report shows `VerifiedScoped`. The `evidence` field of every step starts with `simulated:`. To see the simulation refuse to declare verified what it did not probe, use the single-process demo, where the summary ends up `Partial`.

## Configuration

| Variable | Values | Notes |
|---|---|---|
| `AP_SERVICE` | `control`, `executor`, `worker`, `demo`, `devtoken` | Process to run in the image. |
| `Auth__Mode` | `Entra`, `Development` | Required. The service does not start without it. |
| `Auth__Authority` | Issuer URL | `Entra` mode only. |
| `Auth__Audience` | Expected audience | Defaults to the service's own audience. |
| `Auth__AllowedTenants__0` | Tenant GUID | Required. An empty list is rejected. |
| `Auth__DevSigningKey` | 32 bytes or more | `Development` mode only. `scripts/quickstart.sh` generates one. |
| `Storage__Provider` | `SqlServer`, `InMemory` | Required. |
| `Storage__ConnectionString` | Connection string | `SqlServer` only. |
| `Storage__CreateDatabase` | `true`, `false` | Creates the database if it does not exist. Local stack only. |
| `Adapters__Mode` | `Simulation` | The only mode that exists. |
| `AP_TEST_SQL` | Connection string | Used by `dotnet test`. If set, the SQL Server ledger tests run. If empty, they are skipped and reported as skipped, never as failures. |

## Tests

```bash
dotnet test
```

On 2026-11-11: 78 tests, 78 pass, with `AP_TEST_SQL` pointing to a SQL Server 2022 container (`eval "$(./scripts/sql-local-up.sh)"`). Without that variable, 73 pass and the 5 SQL tests are reported as skipped. The SQL tests prove against the real engine that the ledger table rejects `UPDATE`, `DELETE` and `TRUNCATE`, that verification with the digest passes and that a wrong digest is detected.

The specification defines 20 blocking tests (BT-01 to BT-20). The status of each one, with the names of the tests that cover it and what is missing to make it real evidence, is in [docs/test-plan.md](docs/test-plan.md). Six of them do not have real evidence yet: three are covered in simulation only and three can only be proven on a tenant, in phases C and D.

## Status by phase

| Phase | Content | Status |
|---|---|---|
| A | Environment assessment, architecture, threat model, permission matrix, test plan, costs | Documents written from the official documentation. Every row of the permission matrix is still to be confirmed on a tenant. |
| B | Local vertical slice without credentials | Done and tested. |
| C | Read pilot on a test tenant | Not started. Blocked until the phase A gates are approved. |
| D | Real containment and timing measurements | Not started. |
| E | Hardening and operation | Not started. The Bicep in `infra/bicep` has to be compiled and deployed when the project moves to a real tenant. |

## Documentation

- [Architecture](docs/architecture.md), the [architecture diagram](docs/architecture-diagram.html) and the [decisions (ADR)](docs/adr/README.md)
- [Environment assessment](docs/environment-assessment.md), with what the Microsoft documentation confirms and what it does not
- [Threat model](docs/threat-model.md), [risks](docs/risks.md), [gaps and assumptions](docs/gaps-and-assumptions.md)
- [Permission matrix](docs/permission-matrix.md) and [coverage matrix](docs/coverage-matrix.md)
- [Test plan](docs/test-plan.md), [cost estimate](docs/cost-estimate.md), [backlog](docs/backlog.md)
- [Runbooks](runbooks/README.md) and [infrastructure as code](infra/bicep/README.md)
- [Getting started](GETTING_STARTED.md), [contributing](CONTRIBUTING.md) and [security policy](SECURITY.md)

## Structure

```
src/Core               domain, authorisation engine, services, ports
src/Storage.SqlServer  transactional storage and ledger schema
src/Simulation         simulated Microsoft side, with fault injection
src/Hosting            shared authentication, configuration and composition
src/ControlApi         control plane, for operators
src/PolicyExecutor     data plane, the only point the agent can reach
src/ContainmentWorker  external containment, reads the outbox
src/AdminUi            deferred
samples/LocalSlice     single-process demo
tools/DevToken         development tokens for the local stack
tests/                 Unit, Integration, Security, FailureInjection
infra/bicep            infrastructure sketch, not validated
runbooks/              operating procedures
```

## About the name

The project is called **Agent Portifolio**. The spelling is deliberate and is used consistently in the repository, the namespaces and the image.

## Licence

Apache-2.0. See [LICENSE](LICENSE).
