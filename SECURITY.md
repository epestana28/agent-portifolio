# Security

## Status

This project is a prototype. The Microsoft side (Entra, Graph, SharePoint) is simulated, and the project must not be used to protect agents in production.

## Reporting a vulnerability

Do not open a public issue. Use "Report a vulnerability" in the Security tab of the GitHub repository, which creates a private advisory. Describe the impact, the steps to reproduce and the version or commit.

## Scope

Of particular interest is any way to: execute an action without a valid capability, execute after a suspension, obtain a positive decision while the state store is unavailable, change or delete audit events, act on an agent of another tenant, or obtain a credential or a pre-authenticated URL from the executor.

The `Development` authentication mode and the tokens issued by `devtoken` exist only for the local stack and are out of scope.
