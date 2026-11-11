# ADR 0009: Funcionalidades preview e alternativas documentadas

Estado: proposto. Data de verificação das fontes: 2026-11-11.

## Contexto

Várias funcionalidades de que o desenho depende estão em preview, ou não têm declaração clara de disponibilidade geral. As páginas de preview não têm SLA e podem mudar sem aviso. Um controlo de segurança que dependa de uma API beta pode desaparecer ou mudar de comportamento.

Estado real: nenhum serviço Microsoft real foi chamado. Este ADR regista só o que a pesquisa documental da fase A encontrou. Quando uma funcionalidade não mostra marca de preview nem frase de GA, o estado é NÃO VERIFICADO, e não "GA".

## Decisão

1. Nenhum controlo obrigatório do MVP depende de uma funcionalidade preview sem alternativa documentada e testada.
2. Funcionalidades preview só entram como complemento opcional, sinalizado nos relatórios, nunca como único mecanismo de contenção.
3. Cada funcionalidade da tabela tem alternativa estável ou uma lacuna registada.
4. Antes da fase E, repetir esta verificação. Uma funcionalidade que mude de estado pede revisão deste ADR.

| Funcionalidade | Estado segundo a pesquisa | Alternativa documentada |
|---|---|---|
| ID Protection para agentes | Preview | Monitorização própria e reconciliação, sem bloqueio automático por risco |
| Editor de manifesto do blueprint | Preview | Criação por Graph e procedimento manual documentado |
| Políticas de ciclo de vida de agentes | Preview (lido por excerto) | Revisão periódica própria e estado `Retired` |
| Condição de CA "Agent execution environments" | Preview | Controlo no gateway e no executor, sem esta condição |
| Agent user (Agentic-User) | Preview, programa Frontier | Modo aplicação, que é o do MVP (ADR 0002) |
| CA para agentes e logs de início de sessão via Graph beta | Beta nos exemplos | Controlo no gateway, sem depender de CA para a contenção |
| Desactivar via `isDisabled` (beta) | Beta | `accountEnabled` em v1.0 no `PATCH` de `agentIdentity`, corpo por validar |
| Remover permissões por lista ou item | Só beta | `DELETE` ao nível do site em v1.0, ou revogação de consentimento |
| RBAC for Applications (Exchange) | GA NÃO VERIFICADO | Fora do MVP, que é só SharePoint |
| Selected permissions por nível (lista, item) | GA NÃO VERIFICADO | Selected ao nível do site |
| SQL Ledger no Azure SQL Database | Sem frase literal de GA, sem marca de preview | Azure Confidential Ledger como alternativa inferida, com custos listados no ADR 0004 |
| "Unified model API" e gateway Foundry no APIM | Preview | APIM com políticas padrão, sem estas funções |
| "Disable in all tenants" | Endpoint NÃO VERIFICADO | Desactivação por tenant, em cada tenant |
| SDK de observabilidade do Agent 365 | Descontinuado, em favor da Distro | Usar a Distro, ou OpenTelemetry directo |
| Agent 365 | GA desde 2026-05-01 (Commercial) | Não aplicável |
| Agent ID | "Available for all customers", GA textual NÃO VERIFICADO | APIs em v1.0, risco R-01 mantém-se aberto |

## Consequências

- O produto pode ficar sem alguma capacidade de contenção quando a alternativa é mais fraca. O relatório regista a limitação (`StandingLimitation`) em vez de a esconder.
- Mudanças de comportamento em preview podem partir adaptadores. Os adaptadores ficam atrás de `IContainmentAdapter`, o que isola o impacto.
- A dependência da API beta para remover grants por lista ou item limita a revogação fina. A granularidade de revogação suportada em v1.0 é a do site.
- O custo de manter alternativas é aceite. O risco R-01 (dependência de preview) fica aberto até à fase E.
- Nada neste ADR autoriza afirmar GA sem fonte. A frase "GA NÃO VERIFICADO" é para manter.

## Alternativas rejeitadas

- Usar as funcionalidades preview como mecanismo principal: sem SLA e sem garantia de estabilidade.
- Esperar que tudo esteja em GA antes de começar: bloquearia o projecto. A fase A e B não dependem delas.
- Presumir GA quando a página não mostra preview: a ausência de marca não é prova.
- Ignorar o tema e tratar o estado como detalhe de implementação: o risco de segurança de um controlo preview é do produto.

## Evidência documental

Todas verificadas em 2026-11-11. Muitas foram lidas só por excerto de pesquisa, e esse facto está nas notas da lista de lacunas.

- Agent ID, âmbito e disponibilidade. https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id
- Agent 365 em GA desde 2026-05-01 (Commercial). https://learn.microsoft.com/en-us/microsoft-agent-365/overview
- Conditional Access para agentes, com condições em preview e exemplos em Graph beta. https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id
- ID Protection para agentes, em preview. https://learn.microsoft.com/en-us/entra/id-protection/concept-workload-identity-risk
- Desactivação e `accountEnabled` em v1.0, com tensão entre páginas. https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0
- Remoção de permissões de site em v1.0 e por lista ou item em beta. https://learn.microsoft.com/en-us/graph/api/site-delete-permission?view=graph-rest-1.0
- Agent user no programa Frontier. https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity
- APIM AI gateway e funções em preview. https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities
- SDK de observabilidade do Agent 365 descontinuado. https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability-concepts
- SQL Ledger: páginas sem marca de preview nem frase de GA. https://learn.microsoft.com/en-us/azure/azure-sql/database/ledger-landing
- Exchange RBAC for Applications. https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac

## Estado

Proposto. Reverificar antes de cada gate de fase.
