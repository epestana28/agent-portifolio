# Contributing

## Project rules

1. Nothing is stated about the behaviour of Microsoft services without a Microsoft Learn page and the date it was read. Anything that is not verified is written as "NOT VERIFIED".
2. What is simulated is called simulated, in the code, in the responses and in the documents. A simulation never hides a gap.
3. A containment step is only `Verified` after an independent read. A timeout is `Unknown`.
4. Auditing uses the SQL Server ledger. Do not add a hash chain or a token issuer of your own.
5. The executor only accepts operations from the closed list. Do not add a generic proxy, `$batch` or arbitrary URLs.

## How to work

```bash
dotnet build
dotnet test
eval "$(./scripts/sql-local-up.sh)" && dotnet test   # includes the ledger tests
```

Compiler warnings are errors. A change in behaviour comes with a test. A test that covers one of the 20 blocking tests uses the prefix `BTnn_` in the method name and is reflected in `docs/test-plan.md`.

A new architecture decision comes with an ADR in `docs/adr`. User-visible changes go in `CHANGELOG.md`.

## Commit messages

Format `type(scope): description`, with the types `feat`, `fix`, `docs`, `test`, `chore`, `refactor` and `ci`.
