# Getting started

Pick the path that matches what you want to do. None of them needs Azure or Microsoft 365 credentials, except the last-but-one.

## 1. Demo (2 minutes, no credentials)

Requires the .NET 8 SDK, or Docker.

```bash
git clone https://github.com/epestana28/agent-portifolio.git
cd agent-portifolio
dotnet run --project samples/LocalSlice
```

With Docker only: `docker run --rm ghcr.io/epestana28/agent-portifolio:latest demo`.

It shows registration, two-person approval, blocking, suspension, containment, reactivation and the audit log. The Microsoft side is simulated.

## 2. Docker stack with SQL Server

Requires Docker with Compose, and `curl`.

```bash
./scripts/quickstart.sh
```

It starts SQL Server 2022, the control API, the executor and the containment worker, and then walks the whole flow over HTTP. The control API listens on `127.0.0.1:8081` and the executor on `127.0.0.1:8082`. Stop the stack with `docker compose down`.

## 3. Tests

Requires the .NET 8 SDK.

```bash
dotnet test
```

73 unit and integration tests pass. The 5 SQL ledger tests are reported as skipped, not failed, because no SQL Server is configured.

## 4. Tests including the SQL Server ledger

Requires the .NET 8 SDK and Docker.

```bash
eval "$(./scripts/sql-local-up.sh)"
dotnet test
```

The `eval` sets `AP_TEST_SQL` in your current shell, so run both commands in the same terminal. All 78 tests run, including the ledger tests against a real SQL Server 2022. Remove the throwaway container afterwards with `docker rm -f ap-test-sql`.

## 5. Validate real Entra tokens

Requires an Entra tenant. Create two app registrations with the audiences `api://agent-portifolio-control` and `api://agent-portifolio-executor`, and add the app roles `AgentPortifolio.Reader`, `AgentPortifolio.IncidentOperator`, `AgentPortifolio.PolicyAdmin` and `AgentPortifolio.Auditor` to the first one. Put your tenant id in `AP_TENANT_ID` in the `.env`, then:

```bash
docker compose -f docker-compose.yml -f docker-compose.entra.yml up -d
```

The services now validate real tokens from your tenant. Everything that would be a call to Entra, Graph or SharePoint is still simulated, and this mode has not been exercised against a real tenant by the author.

## 6. Contribute

Requires Git, the .NET 8 SDK and Docker.

```bash
git checkout -b feat/your-change
dotnet build && dotnet test
git commit -m "feat(scope): description"
git push origin feat/your-change
```

Read [CONTRIBUTING.md](CONTRIBUTING.md) for the project rules and [docs/architecture.md](docs/architecture.md) to understand the design.
