<p align="center"><img src="docs/assets/icon.png" width="128" alt="Agent Portifolio"></p>

# Agent Portifolio

Camada de controlo e de evidência para agentes de IA em Azure e Microsoft 365. Dá a cada agente uma carteira (wallet) de capacidades, decide cada acção de forma determinística, regista a decisão num ledger append-only e, num incidente, suspende o agente e diz com rigor o que ficou contido e o que não ficou.

> **Estado: fatia vertical local, com o lado Microsoft em simulação.** O código de autorização, bloqueio, auditoria e orquestração da contenção é real e está testado, incluindo contra um SQL Server 2022 com ledger. Não existe ainda nenhum adaptador real para Entra, Graph ou SharePoint. Nada neste repositório é prova de como os serviços Microsoft se comportam. Ver [Estado por fase](#estado-por-fase).

*English summary: a control and evidence layer for AI agents on Azure and Microsoft 365: capability wallet, deterministic authorisation, append-only SQL ledger audit, suspension and honest containment reporting. The Microsoft side is simulated today; the control logic is real and tested. Documentation is in Portuguese.*

## O problema

Um agente de IA com acesso a dados da empresa levanta três perguntas a que quase ninguém sabe responder com provas. O que pode este agente fazer, exactamente? O que fez? E, se o suspender agora, o que é que deixa mesmo de funcionar e quando?

A terceira é a mais difícil. Desactivar uma identidade no Entra não invalida, por si só, os tokens já emitidos. Um painel que mostra "revogado" depois de uma chamada administrativa bem sucedida está a afirmar algo que não verificou.

## O que faz

- **Carteira de capacidades.** Cada agente tem dono, identidade própria, identidade de executor separada e uma lista curta de capacidades: uma acção de uma lista fechada, um recurso em forma canónica, uma validade. Não há wildcards.
- **Autorização determinística.** O agente nunca fala com o Graph. Pede uma operação ao executor (`POST /actions`), que valida o token, resolve a operação na lista fechada e decide numa transacção com o estado actual. Não há proxy genérico, nem `$batch`, nem URL arbitrário.
- **Falha fechada.** Se a base de estado não responde, a resposta é `state_unavailable` e nada é executado. Não existe cache de decisões positivas.
- **Auditoria em ledger.** A decisão, a intenção e o evento de auditoria são gravados na mesma transacção. Com SQL Server ou Azure SQL, os eventos vão para uma tabela ledger append-only do próprio motor. O projecto não implementa cadeia de hashes própria.
- **Suspensão imediata.** Suspender é uma única transacção: estado, incremento do epoch de revogação, operação de contenção, mensagem na outbox e auditoria. O pedido seguinte do agente é recusado antes de qualquer passo externo. Uma acção já admitida é revalidada antes da chamada externa e cancelada.
- **Contenção com estados honestos.** Cada passo externo (desactivar identidade, remover grants, isolar runtime, sondar tokens emitidos) fica em `Requested`, `Executed`, `Verified`, `Failed` ou `Unknown`. O resumo só é `VerifiedScoped` se todos os passos forem verificados, e traz sempre a lista de incertezas. Um timeout é `Unknown`, nunca sucesso.
- **Reactivação a dois.** Uma pessoa pede, outra aprova, os grants externos têm de ser revalidados depois da suspensão e as capacidades antigas ficam mortas (epoch novo).
- **Reconciliação.** Compara os grants que existem do lado externo com os que a carteira conhece. Um grant largo (por exemplo `Sites.Read.All`) suspende o agente. Um grant directo na identidade do agente baixa a cobertura para `DirectAccessPossible`.

## Experimentar em 2 minutos

Sem credenciais, sem tenant, num só processo:

```bash
docker run --rm ghcr.io/epestana28/agent-portifolio:latest demo
```

Ou, com o .NET 8 SDK: `dotnet run --project samples/LocalSlice`.

A demonstração percorre registo, aprovação a dois, capacidade, leitura, recusas, suspensão, contenção, reactivação e mostra o registo de auditoria.

## Pilha local com Docker

Sobe SQL Server 2022 (com ledger), a API de controlo, o executor e o worker de contenção, e percorre o fluxo todo por HTTP:

```bash
git clone https://github.com/epestana28/agent-portifolio.git
cd agent-portifolio
./scripts/quickstart.sh
```

O script cria um `.env` com valores aleatórios, faz `docker compose up -d`, emite tokens de desenvolvimento e mostra cada resposta. Precisa de `docker` (com compose) e `curl`. A API de controlo fica em `127.0.0.1:8081` e o executor em `127.0.0.1:8082`.

A mesma imagem contém os três serviços e duas ferramentas. A variável `AP_SERVICE` (ou o primeiro argumento) escolhe: `control`, `executor`, `worker`, `demo`, `devtoken`.

### Usar com a sua conta Microsoft Entra

```bash
docker compose -f docker-compose.yml -f docker-compose.entra.yml up -d
```

Com `AP_TENANT_ID` no `.env`, os dois serviços passam a validar tokens reais do seu tenant: assinatura, emissor, audiência, validade e tenant. Precisa de dois registos de aplicação com as audiências `api://agent-portifolio-control` e `api://agent-portifolio-executor`, e dos papéis de aplicação `AgentPortifolio.Reader`, `AgentPortifolio.IncidentOperator`, `AgentPortifolio.PolicyAdmin` e `AgentPortifolio.Auditor` no primeiro.

O que passa a ser real neste modo: a validação dos tokens, a carteira, as decisões, o bloqueio e o ledger. O que continua simulado: tudo o que seria uma chamada ao Entra, ao Graph ou ao SharePoint. O serviço recusa arrancar com `Adapters__Mode` diferente de `Simulation`, para não fingir uma integração que não existe. Este modo não foi exercitado contra um tenant real pelo autor.

### Limites da simulação na pilha Docker

Cada contentor tem o seu próprio ambiente simulado, em memória. O worker de contenção não conhece tokens nem identidades simulados pelos outros processos, pelo que os seus passos dão `Verified` de forma trivial e o relatório aparece como `VerifiedScoped`. O campo `evidence` de cada passo começa por `simulated:`. Para ver a simulação a recusar-se a declarar verificado o que não sondou, use a demonstração num só processo, onde o resumo fica `Partial`.

## Configuração

| Variável | Valores | Notas |
|---|---|---|
| `AP_SERVICE` | `control`, `executor`, `worker`, `demo`, `devtoken` | Processo a correr na imagem. |
| `Auth__Mode` | `Entra`, `Development` | Obrigatório. Sem valor, o serviço não arranca. |
| `Auth__Authority` | URL do emissor | Só em modo `Entra`. |
| `Auth__Audience` | audiência esperada | Por omissão, a do serviço. |
| `Auth__AllowedTenants__0` | GUID do tenant | Obrigatório. Lista vazia é recusada. |
| `Auth__DevSigningKey` | 32 bytes ou mais | Só em modo `Development`. |
| `Storage__Provider` | `SqlServer`, `InMemory` | Obrigatório. |
| `Storage__ConnectionString` | connection string | Só com `SqlServer`. |
| `Storage__CreateDatabase` | `true`, `false` | Cria a base se não existir. Só para a pilha local. |
| `Adapters__Mode` | `Simulation` | Único modo existente. |

## Testes

```bash
dotnet test
```

Em 2026-11-11: 78 testes, 78 passam, com `AP_TEST_SQL` a apontar para um SQL Server 2022 em contentor (`eval "$(./scripts/sql-local-up.sh)"`). Sem essa variável, 73 passam e os 5 testes de SQL ficam marcados como ignorados. Os testes de SQL provam contra o motor real que a tabela ledger rejeita `UPDATE`, `DELETE` e `TRUNCATE`, que a verificação com o digest passa e que um digest errado é detectado.

A especificação define 20 testes bloqueantes (BT-01 a BT-20). O estado de cada um, com os nomes dos testes que o cobrem e o que falta para ser prova real, está em [docs/test-plan.md](docs/test-plan.md). Seis deles não têm ainda prova real: três estão cobertos só em simulação e três só podem ser provados num tenant, nas fases C e D.

## Estado por fase

| Fase | Conteúdo | Estado |
|---|---|---|
| A | Avaliação do ambiente, arquitectura, modelo de ameaças, matriz de permissões, plano de testes, custos | Documentos escritos a partir da documentação oficial. Todas as linhas da matriz de permissões estão por confirmar num tenant. |
| B | Fatia vertical local sem credenciais | Feita e testada. |
| C | Piloto de leitura num tenant de teste | Por fazer. Bloqueada até aprovação dos gates da fase A. |
| D | Contenção real e medição de tempos | Por fazer. |
| E | Endurecimento e operação | Por fazer. O Bicep em `infra/bicep` nunca foi compilado nem implantado. |

## Documentação

- [Arquitectura](docs/architecture.md) e [decisões (ADR)](docs/adr/README.md)
- [Avaliação do ambiente](docs/environment-assessment.md), com o que a documentação Microsoft confirma e o que não confirma
- [Modelo de ameaças](docs/threat-model.md), [riscos](docs/risks.md), [lacunas e pressupostos](docs/gaps-and-assumptions.md)
- [Matriz de permissões](docs/permission-matrix.md) e [matriz de cobertura](docs/coverage-matrix.md)
- [Plano de testes](docs/test-plan.md), [estimativa de custos](docs/cost-estimate.md), [backlog](docs/backlog.md)
- [Runbooks](runbooks/README.md) e [infra-estrutura como código](infra/bicep/README.md)

## Estrutura

```
src/Core               domínio, motor de autorização, serviços, portas
src/Storage.SqlServer  armazenamento transaccional e esquema com ledger
src/Simulation         lado Microsoft simulado, com injecção de falhas
src/Hosting            autenticação, configuração e composição comuns
src/ControlApi         plano de controlo, para operadores
src/PolicyExecutor     plano de dados, o único ponto que o agente alcança
src/ContainmentWorker  contenção externa, lê a outbox
src/AdminUi            adiada
samples/LocalSlice     demonstração num só processo
tools/DevToken         tokens de desenvolvimento para a pilha local
tests/                 Unit, Integration, Security, FailureInjection
infra/bicep            esboço de infra-estrutura, não validado
runbooks/              procedimentos de operação
```

## Sobre o nome

"Portifolio" é a grafia escolhida para o projecto e é usada de propósito no repositório, nos namespaces e na imagem.

## Licença

Apache-2.0. Ver [LICENSE](LICENSE).
