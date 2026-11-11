# Registo de alterações

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-PT/1.1.0/) e o projecto usa [versionamento semântico](https://semver.org/lang/pt-BR/).

## [Por lançar]

## [0.1.0] - 2026-11-11

### Adicionado

- Domínio da carteira de capacidades, motor de autorização determinístico e lista fechada de operações (`sharepoint.document.read`, `sharepoint.document.list`).
- Pipeline de execução em três transacções, com idempotência, revalidação antes da chamada externa e falha fechada.
- Suspensão numa única transacção com epoch de revogação, outbox e operação de contenção.
- Orquestrador de contenção com estados por passo, repetição, respeito por `Retry-After` e dead-letter.
- Reactivação com pedido e aprovação por pessoas diferentes e revalidação de grants.
- Reconciliação de grants externos com detecção de grants largos e de acesso directo.
- Armazenamento em SQL Server 2022 e Azure SQL, com auditoria numa tabela ledger append-only.
- API de controlo, executor de política e worker de contenção em ASP.NET Core, com validação de tokens Entra ou de desenvolvimento.
- Lado Microsoft simulado, identificado como tal, com injecção de falhas.
- Imagem Docker única, `docker-compose.yml` com SQL Server e script de arranque rápido.
- Testes unitários, de integração, de segurança e de injecção de falhas, incluindo testes contra SQL Server real.
- Documentação da fase A, ADR, runbooks e esboço de Bicep.

### Limitações conhecidas

- Não existem adaptadores reais para Entra, Graph ou SharePoint.
- O Bicep não foi compilado nem implantado.
- O modo de autenticação Entra não foi exercitado contra um tenant real.
