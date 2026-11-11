# Backlog

Projecto: Agent Portifolio. Documento da fase A. Data de verificação das fontes Microsoft: 2026-11-11.

Ordem: por risco, o maior primeiro, dentro de cada fase. As fases seguem a secção 16 da especificação, de A a E. Os riscos referidos (`R-nn`) estão em `risks.md`, as ameaças (`T-nn`) e os testes (`BT-nn`, `TC-nn`) em `threat-model.md`, as lacunas (`L-nn`) e pressupostos (`P-nn`) em `gaps-and-assumptions.md`.

## 1. Estado real

Nenhuma fase avança sem o gate da anterior aprovado por uma pessoa. O estado abaixo não foi inflacionado.

| Fase | Estado | Gate (secção 16 da especificação) | Estado do gate |
|---|---|---|---|
| A. Validação | Documentos escritos a partir de documentação oficial, sem tenant. Campos que dependem do tenant ficam "POR PREENCHER NO TENANT". | Aprovação de âmbito, licenças e permissões. | Por aprovar por humano. |
| B. Fatia local | Implementada em .NET 8, sem credenciais reais. O mundo Microsoft é simulado e assinalado como simulação. | Invariantes e concorrência testados. | Por aprovar por humano. Suite local por confirmar (ver B-01). |
| C. Azure de teste | Por fazer. Bicep escrito, nunca aplicado. | Prova de impossibilidade de acesso directo no perfil forte. | Não aplicável. |
| D. Contenção | Por fazer. Nenhum adaptador real para Entra nem para Graph. | Cobertura verificada, incertezas visíveis e runbook independente. | Não aplicável. |
| E. Piloto | Por fazer. | Aprovação humana de produção. Zero riscos críticos não mitigados ou aceites formalmente. | Não aplicável. |

Documentos da fase A previstos na especificação: `environment-assessment.md`, `architecture.md`, `threat-model.md`, `permission-matrix.md`, `coverage-matrix.md`, `cost-estimate.md`, ADRs e riscos. Este conjunto contém o threat model, os riscos, as lacunas e pressupostos, este backlog e os nove ADRs.

## 2. O que já existe na Microsoft e o mínimo a construir

Derivada da secção 3 da especificação (regra Microsoft-first), com o estado verificado da documentação. "Código próprio admitido" é o limite: o que fica fora desta coluna não se constrói.

Cada facto Microsoft leva chave `Mxx` que remete para a tabela de fontes no fim (URL do Microsoft Learn e data 2026-11-11).

| Necessidade | Já existe na Microsoft (estado verificado) | Mínimo a construir | Onde no repositório |
|---|---|---|---|
| Identidade de agente | Entra Agent ID, "available for all Microsoft Entra customers", APIs Graph em `v1.0`. GA textual NÃO VERIFICADO [M60] [M18]. Agent identity é um service principal e as credenciais vivem no blueprint [M14]. | Adaptador por tipo de identidade (`IdentityType`) e correlação com o agente registado. | `src/Core` (modelo), adaptadores em `src/Simulation` (hoje só simulados) |
| Workloads existentes | Managed identities e service principals. Managed identity: sem disable documentado, cache de cerca de 24 horas, sem refresh forçado [M09]. | Adaptadores separados por tipo. Isolamento do runtime e remoção de grants. | `src/ContainmentWorker`, `src/Simulation` |
| Inventário e responsáveis | Agent 365 em GA desde 2026-05-01, segmento Commercial, licença por utilizador [M16]. Criar a identidade Entra não regista a instância no Agent 365 [M15]. | Vista agregada e campos de cobertura. Não duplicar o inventário nativo. | `src/ControlApi` |
| Entrada governada | Azure API Management com `validate-azure-ad-token` [M35]. O AI gateway governa tráfego de modelos e ferramentas, não a semântica de Graph [M34]. | Modelo de política APIM (audiência, aplicação cliente, negar `$batch`). Validação de capacidades e recursos no executor. | `infra/bicep`, `src/PolicyExecutor` |
| Permissões SharePoint | Selected permissions: `Sites.Selected`, `Lists.SelectedOperations.Selected`, `ListItems.SelectedOperations.Selected`, `Files.SelectedOperations.Selected`. Consentimento sem atribuição no recurso não dá acesso [M22]. GA por nível NÃO VERIFICADO. | Orquestração de concessão e verificação. Identidade que atribui separada do executor. | `src/ContainmentWorker`, `src/ControlApi` (por fazer na fase C) |
| Correio (fase posterior) | Exchange RBAC for Applications. Soma-se às permissões do Entra. Não existe papel só de rascunhos [M32]. GA NÃO VERIFICADO. | Executor restrito a operações permitidas. Testes negativos. Fora do MVP. | Não iniciado |
| Histórico verificável | Azure SQL Database Ledger, tabelas append-only [M44], digests automáticos [M41], verificação [M40]. Evidência de adulteração, não prevenção [M39]. | Esquema `ap.AuditEvent` e eventos de domínio. | `src/Storage.SqlServer` |
| Digests independentes | Blob imutável com política `locked` [M46]. Azure Confidential Ledger como opção, com limites [M49]. | Agendamento e verificação complementar. Alertas. | `infra/bicep`, `runbooks` |
| Segredos | Key Vault. Identidades sem segredo quando possível. | Nenhum cofre próprio. | `infra/bicep` |
| Observabilidade | Agent 365 com OpenTelemetry. O SDK de observabilidade do Agent 365 está deprecated, recomendada a Microsoft OpenTelemetry Distro [M15]. Azure Monitor e Application Insights. | Instrumentação e correlação do serviço de controlo e do executor. Não substitui a ledger. | `src/*` |
| Detecção e dados | Os dados de observabilidade de agentes registados aparecem no Microsoft Defender (vistas de actividade e `CloudAppEvents`), no centro de administração Microsoft 365 e no Purview [M15]. Sentinel não foi pesquisado. Cobertura efectiva NÃO VERIFICADA. | Integração, não substituição. Por avaliar na fase D ou E. | Não iniciado |
| Infraestrutura | Bicep. Azure Policy não foi pesquisado: NÃO VERIFICADO. | Módulos do produto. | `infra/bicep` |
| Autorização fina de operações | Nenhuma capacidade documentada para semântica de Graph, SharePoint ou Exchange no APIM [M34]. | Tudo: `AuthorizationEngine`, `OperationCatalog`, `ResourceCanonicalizer`, `ExecutionService`. | `src/Core` |
| Carteira, capacidades, epoch | Não existe equivalente. | Tudo: modelo, `IWalletStore.TransactAsync`, suspensão, outbox. | `src/Core`, `src/Storage.SqlServer` |
| Estados de contenção e relatório por passo | Não existe equivalente. | Tudo: `ContainmentOperation`, estados separados, sondas. | `src/Core`, `src/ContainmentWorker` |

Resumo do mínimo: o produto constrói a camada de carteira, autorização, contenção e evidência. Não constrói identidade, ledger, gateway, cofre, nem emissor de tokens.

## 3. Fase A: validação

Gate: aprovação de âmbito, licenças e permissões. Estado: por aprovar por humano.

Itens por risco, o maior primeiro. Tarefas só de decisão ou de preenchimento.

| Id | Item | Risco | Depende de | Saída |
|---|---|---|---|---|
| A-01 | Decidir a retenção, a pseudonimização e a eliminação de dados na ledger e nos digests, e `isLedgerOn`, antes de qualquer lock ou criação da base. | R-05 | Responsável de protecção de dados | Decisão registada |
| A-02 | Aprovar o tratamento da contradição de Selected: assumir `Sites.Selected` com `read` e exigir confirmação endpoint a endpoint na fase C. Proibir `Files.Read.All` e `Sites.Read.All`. | R-07 | Dono do projecto | Decisão registada |
| A-03 | Confirmar licenças (Agent 365, Entra P1 ou P2, E7) e custos incrementais. | R-04, R-15 | Dono do projecto, comercial | Licenças aprovadas |
| A-04 | Aceitar ou recusar cada dependência em preview e em beta, com a alternativa de cada uma (ADR 0009). | R-01 | Dono do projecto | ADR 0009 aprovado |
| A-05 | Decidir destino dos digests: Blob imutável (baseline) ou Confidential Ledger. | R-14 | Segurança | ADR 0004 aprovado |
| A-06 | Atribuir owners das ameaças e dos riscos. | R-25 | Dono do projecto | Tabela com nomes |
| A-07 | Aprovar o âmbito do piloto: um tenant, um runtime, um agente de leitura, uma biblioteca, teste negativo noutro recurso. | R-17 | Dono do projecto | Âmbito aprovado |
| A-08 | Decidir se o Agent 365 entra no âmbito e se o agent user fica fora. | R-11 | Dono do projecto | Decisão registada |
| A-09 | Definir o intervalo da reconciliação periódica e o RPO e RTO alvo. | R-21 | Dono do projecto, plataforma | Valores aprovados |
| A-10 | Preencher os campos "POR PREENCHER NO TENANT" de `environment-assessment.md`. | R-17 | Acesso ao tenant | Documento completo. Bloqueado sem tenant. |
| A-11 | Reler por inteiro as páginas verificadas só por excerto antes de as citar numa decisão (L-42). | R-01 | Nenhuma | Fontes completas |
| A-12 | Aprovar o gate da fase A. | todos | A-01 a A-09 | Gate aprovado por humano |

## 4. Fase B: fatia local

Gate: invariantes e concorrência testados. Estado: implementada, gate por aprovar por humano.

Implementado: núcleo (`src/Core`), armazenamento SQL Server (`src/Storage.SqlServer`), mundo simulado com injecção de falhas (`src/Simulation`), API de controlo, executor, worker de contenção, imagem Docker e docker compose com SQL Server 2022. Nada disto usa credenciais reais.

Itens por risco, o maior primeiro. O trabalho que falta é fechar os testes e confirmar o estado.

| Id | Item | Risco | Saída |
|---|---|---|---|
| B-01 | FEITO em 2026-11-11, ver `test-plan.md`. Confirmado contra a suite: 78 testes, 78 passados com `AP_TEST_SQL`, 73 passados e 5 ignorados sem ela. Cada teste de BT tem o prefixo `BTnn_` no método. BT-06, BT-13 e BT-14 só existem em simulação. BT-05, BT-17 e BT-20 não têm prova local (BT-20 não tem teste). | R-16 | Tabela de BT com estado confirmado |
| B-02 | Testes de concorrência: suspensão contra admissão e execução (BT-03, BT-04), com acções em curso documentadas. FEITO em memória (`BT04_`). Falta a corrida de suspensão contra SQL real: só o limite de acções e a idempotência têm teste concorrente em SQL real. | R-02 | Gate de concorrência |
| B-03 | TC-06 (batch, `$expand`, `$select`, `Location` e `downloadUrl` nunca devolvidos) e TC-05 (argumentos tipo URL). FEITO em parte: `Generic_proxy_batch_and_arbitrary_urls_are_impossible` e `BT06_`. Não há teste dedicado a `$expand`, `$select` nem `Location`. | R-09 | Testes unitários |
| B-04 | TC-07: variantes de grafia do recurso, todas recusadas. FEITO: `Non_canonical_resources_are_rejected_not_normalised`. | R-17 | Teste unitário |
| B-05 | TC-14: mensagem de epoch anterior descartada como `superseded` depois da reactivação. FEITO: `Old_containment_message_never_acts_on_a_reactivated_agent_and_never_reactivates`. | R-18 | Teste local |
| B-06 | TC-09 e TC-10: reactivação por dois PolicyAdmin distintos, e identidade de execução única por agente. TC-09 FEITO. TC-10 POR FAZER: o código só recusa o executor igual à identidade do agente, não dois agentes com o mesmo `ExecutorObjectId`. | R-20, R-12 | Testes locais |
| B-07 | TC-18: falhas injectadas nos adaptadores nunca produzem `VerifiedScoped`. FEITO: `Repeated_failure_is_bounded_and_reported_as_failed` e `Suspended_never_becomes_verified_without_external_steps`. | R-16 | Teste local com falhas injectadas |
| B-08 | TC-01, TC-04, TC-08: argumentos hostis, capacidade cruzada, isolamento de tenant. FEITO em parte: `Unknown_missing_or_extra_arguments_are_rejected`, `Capability_of_another_agent_or_tenant_never_matches`, `Operator_of_another_tenant_cannot_see_or_touch_the_agent`. Falta confirmar que a resposta de um agente de outro tenant é igual à de um agente inexistente. | R-12 | Testes locais |
| B-09 | Assinalar a simulação em cada adaptador simulado e na documentação. FEITO no código (cabeçalho `X-AgentPortifolio-Simulation`, aviso no arranque, prefixo `SIMULATED` no teste de BT-14). Rever os textos. | R-16 | Revisão |
| B-10 | Aprovar o gate da fase B. | R-16 | Gate aprovado por humano |
| B-11 | Decidir `Storage:EnsureSchema`: por omissão é `true` e criar a tabela ledger exige `ENABLE LEDGER`, que o desenho não dá a nenhuma identidade de execução. O Bicep põe `Storage__EnsureSchema=false` e o esquema aplica-se com conta administrativa. | R-05 | Decisão e, se for o caso, omissão `false` no código |
| B-12 | Ligar o que existe e nenhum host chama: `ExecutionService.SweepStaleActionsAsync`, reconciliação periódica, e contenção ao retirar um agente (`retire` só sobe a epoch). | R-18 | Agendamento e testes |

## 5. Fase C: Azure de teste

Gate: prova de impossibilidade de acesso directo no perfil forte. Estado: por fazer. Bloqueada por: gate da fase A aprovado, subscrição e tenant (ver `gaps-and-assumptions.md`, secção 4.3).

Itens por risco, o maior primeiro.

| Id | Item | Risco | Testes | Bloqueado por |
|---|---|---|---|---|
| C-01 | Provar a impossibilidade de acesso directo: agente sem credencial Graph, sem rota de rede, backend não contornável. | R-12, R-06 | BT-05, BT-06, TC-17, TC-20 | Subscrição, Bicep |
| C-02 | Validar a matriz de permissões endpoint a endpoint para as quatro operações, com recurso autorizado e recurso negativo. Decidir as contradições L-19, L-20 e L-21. | R-07 | BT-01 | Tenant |
| C-03 | Validar `tid` e `oid` de um token real de agent identity, e os formatos de site, drive e item. | R-17 | BT-02, TC-07 | Tenant, agent identity de teste |
| C-04 | Modelo de política APIM: `validate-azure-ad-token` com audiência e aplicação cliente, negar `$batch`. Executor valida o token. | R-06, R-09 | TC-02, TC-03, TC-06 | Escalão do APIM |
| C-05 | Observar os anfitriões do 302 no SharePoint Online e fixar a lista de saída do executor. | R-08 | TC-05 | Tenant |
| C-06 | SQL Ledger: tabela append-only, digests automáticos para Blob com política `locked`, depois de a retenção estar aprovada. Lock como passo manual. | R-05, R-14 | BT-16, TC-19 | A-01, subscrição |
| C-07 | Alertas de digests: idade, falha de exportação, queda de uploads com sucesso, resultado da verificação. | R-14 | TC-19 | C-06 |
| C-08 | Verificação agendada da ledger com Azure Automation ou job próprio (Entra-only). | R-19 | BT-16 | Decisão A-05 |
| C-09 | Outbox para Service Bus: duplicate detection, `maxDeliveryCount`, tratamento da DLQ, consumidores idempotentes. | R-18 | BT-09, BT-10, TC-14 | Subscrição |
| C-10 | Restauro e failover: o agente suspenso continua suspenso, RPO validado, reconciliação antes de reabrir tráfego. | R-21 | BT-17 | Azure SQL com geo-replicação |
| C-11 | Medir o SLO de 5 segundos de recusa de novas admissões após suspensão persistida, com p50, p95 e p99. | R-23 | BT-03 | APIM e executor reais |
| C-12 | Compilar o Bicep, `what-if` e aplicação com aprovação. Registar as `apiVersion` aceites (L-39). | R-15, R-17 | nenhum | Subscrição, aprovação de custos |
| C-13 | Aprovar o gate da fase C. | R-12 | C-01 a C-12 | Gate aprovado por humano |

## 6. Fase D: contenção

Gate: cobertura verificada, incertezas visíveis e runbook independente. Estado: por fazer. Bloqueada por: gate da fase C aprovado e tenant com agent identities de teste.

Itens por risco, o maior primeiro.

| Id | Item | Risco | Testes | Bloqueado por |
|---|---|---|---|---|
| D-01 | BT-14: obter um token antes da desactivação, desactivar e testar o token num recurso de teste. Medir por tipo de identidade (agent identity, service principal, managed identity) e por recurso. Reportar o resultado real. | R-02 | BT-14 | Tenant |
| D-02 | Adaptador real de desactivação Entra para agent identity. Validar o corpo do pedido e o efeito de `accountEnabled` (L-02, L-03). | R-02 | BT-13, BT-14 | Agent ID Administrator |
| D-03 | Adaptador real de remoção de grants Selected, com verificação por sonda. Medir o atraso até à recusa. Tratar a assimetria criar e remover (L-21). | R-07, R-12 | BT-07 | Tenant |
| D-04 | Isolamento de runtime e de managed identity sem disable: paragem do runtime, remoção de grants, rotação de credenciais quando aplicável. | R-03 | BT-14 | Runtime de teste |
| D-05 | BT-13 real: desactivar uma agent identity e verificar que as irmãs do blueprint e o agent user mantêm o estado esperado. Medir a cascata (L-05, L-08). | R-13 | BT-13 | Blueprint com duas agent identities |
| D-06 | Estudar a relação entre o bloqueio no Agent 365 e a desactivação no Entra (L-06). | R-11 | nenhum | Agent 365 licenciado, se aprovado |
| D-07 | Throttling real: estados parciais visíveis, `Retry-After` respeitado. | R-22 | BT-12 | Tenant |
| D-08 | Relatório de contenção por recurso, com `VerifiedScoped` só com âmbito explícito e sonda. | R-16 | BT-11, BT-12 | D-02 a D-04 |
| D-09 | Break-glass independente da aplicação: documentar e ensaiar com a aplicação em baixo. | R-20 | BT-20 | Runbook |
| D-10 | Runbook que permita a uma pessoa independente repetir os testes. | R-16 | todos | D-01 a D-09 |
| D-11 | Reconciliação real: grants observados contra registados, grants amplos, acessos directos. | R-12 | BT-15 | D-03 |
| D-12 | Aprovar o gate da fase D. | R-02 | D-01 a D-11 | Gate aprovado por humano |

## 7. Fase E: piloto

Gate: aprovação humana de produção. Zero riscos críticos não mitigados ou aceites formalmente. Estado: por fazer.

Itens por risco, o maior primeiro.

| Id | Item | Risco |
|---|---|---|
| E-01 | Decidir formalmente todos os riscos de `risks.md` (aceitar, mitigar ou bloquear), com decisor nomeado. | Todos |
| E-02 | Revisão de segurança independente do conjunto. | R-06, R-12 |
| E-03 | Recuperação: restauro, failover, RPO e RTO aprovados e ensaiados. | R-21 |
| E-04 | Retenção e eliminação: política aprovada e aplicada antes do lock em produção. | R-05 |
| E-05 | Custos reais contra a estimativa. | R-15 |
| E-06 | Carga, com o limite de que testes de carga não são permitidos em SharePoint Online [M31]. | R-22 |
| E-07 | Operação: alertas mínimos (acesso directo descoberto, drift, tentativa pós-suspensão, conta sem responsável, grant amplo, falha de contenção, ledger ou digest indisponível, alteração administrativa sensível), responsáveis e ensaio. | R-24, R-18 |
| E-08 | Aprovar o gate da fase E. | Todos |

## 8. Definição de concluído do MVP

Secção 18 da especificação. O MVP só está concluído quando um agente real, num ambiente de teste, lê apenas um recurso autorizado, e:

- todas as acções relevantes têm evidência;
- a suspensão bloqueia novas admissões;
- o runtime e os grants são tratados segundo os adaptadores;
- os limites dos tokens existentes são medidos;
- os acessos alternativos são eliminados ou explicitamente classificados;
- uma pessoa independente consegue repetir os testes a partir do runbook.

Uma API com resposta 200, um painel verde ou uma demonstração com mocks não provam contenção. Hoje (fase B) nenhuma destas condições está provada com um agente real.

## 9. Fontes

Todas verificadas em 2026-11-11. Prefixo: `https://learn.microsoft.com/en-us/`. "Leitura" indica se a página foi lida por inteiro ou só por excerto de pesquisa.

| Chave | Caminho | Leitura |
|---|---|---|
| M09 | `entra/identity/managed-identities-azure-resources/managed-identities-faq` | inteira |
| M14 | `microsoft-agent-365/developer/identity` | inteira |
| M15 | `microsoft-agent-365/developer/observability-concepts` | inteira |
| M16 | `microsoft-agent-365/overview` | inteira |
| M18 | `graph/api/resources/agentidentity?view=graph-rest-1.0` | inteira |
| M22 | `graph/permissions-selected-overview` | inteira |
| M31 | `sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online` | inteira |
| M32 | `exchange/permissions-exo/application-rbac` | inteira |
| M34 | `azure/api-management/genai-gateway-capabilities` | inteira |
| M35 | `azure/api-management/validate-azure-ad-token-policy` | inteira |
| M39 | `sql/relational-databases/security/ledger/ledger-overview` | inteira |
| M40 | `sql/relational-databases/security/ledger/ledger-database-verification` | inteira |
| M41 | `sql/relational-databases/security/ledger/ledger-digest-management` | inteira |
| M44 | `sql/relational-databases/security/ledger/ledger-append-only-ledger-tables` | inteira |
| M46 | `azure/storage/blobs/immutable-storage-overview` | inteira |
| M49 | `azure/confidential-ledger/limits` | inteira |
| M60 | `entra/agent-id/what-is-microsoft-entra-agent-id` | inteira |
