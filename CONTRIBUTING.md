# Contribuir

## Regras do projecto

1. Nada se afirma sobre o comportamento de serviços Microsoft sem uma página do Microsoft Learn e a data em que foi lida. O que não está verificado escreve-se "NÃO VERIFICADO".
2. O que é simulado diz-se simulado, no código, nas respostas e nos documentos. Uma simulação nunca serve para esconder uma lacuna.
3. Um passo de contenção só é `Verified` com uma leitura independente. Um timeout é `Unknown`.
4. A auditoria usa o ledger do SQL Server. Não se acrescenta uma cadeia de hashes própria nem um emissor próprio de tokens.
5. O executor só aceita operações da lista fechada. Não se acrescenta proxy genérico, `$batch` nem URL arbitrário.

## Como trabalhar

```bash
dotnet build
dotnet test
eval "$(./scripts/sql-local-up.sh)" && dotnet test
```

Os avisos do compilador são erros. Uma alteração de comportamento traz um teste. Um teste que cobre um dos 20 testes bloqueantes usa o prefixo `BTnn_` no nome do método e é reflectido em `docs/test-plan.md`.

Uma decisão de arquitectura nova traz um ADR em `docs/adr`. As alterações visíveis entram em `CHANGELOG.md`.

## Mensagens de commit

Formato `tipo(âmbito): descrição`, com os tipos `feat`, `fix`, `docs`, `test`, `chore`, `refactor` e `ci`.
