# Registo de decisões de arquitectura

Todas as decisões estão em estado proposto. Nenhuma foi aprovada por um humano. A aprovação faz parte do gate da fase A.

Formato de cada ADR: Contexto, Decisão, Consequências, Alternativas rejeitadas, Evidência documental (URL e data 2026-11-11) e Estado.

Estado real do projecto à data: a fase A são documentos escritos a partir de documentação oficial e sem tenant, e os campos que dependem do tenant ficam "POR PREENCHER NO TENANT". A fase B é uma fatia vertical local em .NET 8, sem credenciais reais, com o mundo Microsoft simulado. As fases C, D e E não foram executadas. O Bicep está escrito e nunca foi aplicado.

| ADR | Título | Estado |
|---|---|---|
| [0001](0001-microsoft-first.md) | Microsoft-first | proposto |
| [0002](0002-identidade-entra-agent-id-e-adaptadores.md) | Identidade com Entra Agent ID e adaptadores separados | proposto |
| [0003](0003-capacidades-opacas-sem-segundo-oauth.md) | Capacidades opacas, sem segundo OAuth | proposto |
| [0004](0004-sql-ledger-append-only-e-digests.md) | SQL Ledger append-only e digests | proposto |
| [0005](0005-outbox-na-base-de-dados-e-service-bus.md) | Outbox na base de dados e Service Bus | proposto |
| [0006](0006-sharepoint-selected-permissions.md) | SharePoint com Selected permissions | proposto |
| [0007](0007-falha-fechada-sem-cache-positiva.md) | Falha fechada, sem cache positiva | proposto |
| [0008](0008-estados-de-contencao-separados.md) | Estados de contenção separados | proposto |
| [0009](0009-funcionalidades-preview.md) | Funcionalidades preview e alternativas documentadas | proposto |

Documentos relacionados: [threat-model.md](../threat-model.md), [gaps-and-assumptions.md](../gaps-and-assumptions.md), [risks.md](../risks.md) e [backlog.md](../backlog.md).
