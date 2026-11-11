# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [semantic versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-11-11

### Added

- Capability wallet domain, deterministic authorisation engine and closed list of operations (`sharepoint.document.read`, `sharepoint.document.list`).
- Three-transaction execution pipeline, with idempotency, revalidation before the external call and fail-closed behaviour.
- Suspension in a single transaction with revocation epoch, outbox and containment operation.
- Containment orchestrator with per-step states, retries, `Retry-After` handling and dead-letter.
- Reactivation with request and approval by different people and grant revalidation.
- Reconciliation of external grants, with detection of broad grants and direct access.
- Storage on SQL Server 2022 and Azure SQL, with auditing in an append-only ledger table.
- Control API, policy executor and containment worker on ASP.NET Core, with validation of Entra or development tokens.
- Simulated Microsoft side, identified as such, with fault injection.
- Single Docker image, a `docker-compose.yml` with SQL Server and a quick-start script.
- Unit, integration, security and fault-injection tests, including tests against a real SQL Server.
- Phase A documentation, ADRs, runbooks and a Bicep sketch.

### Known limitations

- There are no real adapters for Entra, Graph or SharePoint.
- The Bicep has not been compiled or deployed.
- The Entra authentication mode has not been exercised against a real tenant.
