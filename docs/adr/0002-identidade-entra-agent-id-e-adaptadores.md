# ADR 0002: Identidade com Entra Agent ID e adaptadores separados

Estado: proposto. Data de verificação das fontes: 2026-11-11.

## Contexto

Os agentes podem ter identidades de natureza diferente: Entra Agent ID, managed identity e service principal. Têm mecanismos de desactivação, controlos e janelas de revogação diferentes. A documentação lida não garante um botão equivalente em todos os casos.

O modelo Entra Agent ID tem quatro objectos: blueprint, blueprint principal, agent identity e agent user. As credenciais vivem só no blueprint e são partilhadas pelas identidades filhas. Um blueprint pode ter muitas identidades.

Estado real: o código tem `IdentityType` com os três tipos e `ExecutorObjectId` por agente. Os adaptadores de contenção para Entra são simulados. Nenhum foi testado contra um tenant.

## Decisão

1. Preferir identidade individual nativa Entra Agent ID quando suportada. Confirmar também o registo no Agent 365: criar a identidade não regista a instância.
2. Um adaptador por tipo de identidade (`EntraAgentId`, `ManagedIdentity`, `ServicePrincipal`), atrás de `IContainmentAdapter`. Nenhum adaptador finge equivalência com outro.
3. Distinguir agente, conta associada (agent user), blueprint e principal do blueprint. A desactivação por omissão é do agente individual. Desactivar um blueprint ou o tenant exige âmbito visível, impacto e aprovação distintos.
4. Mapear explicitamente contas e credenciais associadas. Não presumir que desaparecem com a identidade.
5. Cada agente tem o seu `ExecutorObjectId`. Não se reutiliza a mesma managed identity user-assigned entre runtimes com âmbitos incompatíveis. Grants no próprio agente contam como caminho directo e baixam a cobertura para `DirectAccessPossible`.
6. Não eliminar identidades como primeira acção de emergência. Para managed identities, documentar paragem ou isolamento do runtime, remoção de grants, rotação de credenciais quando aplicável e o efeito de tokens já emitidos.
7. A identidade do chamador vem do token validado (`tid` e `oid`), nunca de `agent_id` no corpo. Os claims dos tokens de agent identity são pressuposto: POR VALIDAR NO TENANT.
8. MVP em modo aplicação (a agent identity é o sujeito do token). Agent user e modo em nome do utilizador ficam fora do MVP.

## Consequências

- Revogar um agente que use os três modos (aplicação, em nome do utilizador, agent user) exige actuar em sujeitos de token distintos. A cascata da desactivação para o agent user é NÃO VERIFICADA.
- A managed identity tem a janela de revogação mais longa e menos controlos documentados. O adaptador é de isolamento e remoção de grants, não de disable.
- O corpo exacto do pedido que desactiva uma agent identity não está num exemplo oficial. É provável `{"accountEnabled": false}` (inferência) e fica por validar na fase D.
- Existem dois planos de controlo possíveis (Entra e centro de administração Microsoft 365 do Agent 365). Se um altera o outro é NÃO VERIFICADO. O relatório trata-os como passos separados.
- Mais adaptadores a manter, com testes de contenção por tipo.

## Alternativas rejeitadas

- Um único adaptador de "disable" para todos os tipos: a documentação não garante equivalência (managed identity sem disable documentado).
- Eliminar a identidade como primeira acção: perde-se metadados e evidência. Os service principals e managed identities eliminados mantêm tokens válidos até ao fim, e a Microsoft documenta soft delete de 30 dias.
- Desactivar o blueprint por omissão: afecta todas as identidades filhas.
- Conditional Access de âmbito tenant como mecanismo principal: não altera objectos, não impede criar identidades, não se aplica à aquisição de token pelo blueprint para criar identidades, e as condições de agentes usam Graph `beta` nos exemplos.
- Usar agent user no MVP: a documentação do Agent 365 limita-o ao programa Frontier em preview.

## Evidência documental

Todas verificadas em 2026-11-11.

- Modelo de objectos e definições (blueprint, principal, agent identity, agent user). https://learn.microsoft.com/en-us/entra/agent-id/key-concepts
- Agent identity é service principal, é single-tenant, as credenciais estão no blueprint ("Treat each blueprint as a credential boundary"), cada identidade exige pelo menos um sponsor. https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity
- Desactivar uma agent identity bloqueia acesso e emissão de tokens, e desactivar um blueprint bloqueia as existentes e impede novas. https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
- Desactivação por objecto sem afectar outros, e políticas tenant-wide que não impedem criação de identidades. https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities
- `PATCH /servicePrincipals/{id}/microsoft.graph.agentIdentity`, permissão `AgentIdentity.EnableDisable.All`, papel Agent ID Administrator. https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0
- `accountEnabled` na referência de `agentIdentity` ("no users are able to sign in to this app"), em tensão com as páginas de administração (C10 da pesquisa). https://learn.microsoft.com/en-us/graph/api/resources/agentidentity?view=graph-rest-1.0
- Criar a identidade Entra não regista a instância no Agent 365. https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability-concepts
- Managed identity: só service principal, cache de tokens de cerca de 24 horas sem refresh forçado, tokens de identidade eliminada válidos até à expiração, soft delete de 30 dias. https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq
- Managed identities não são cobertas por Conditional Access. https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity
- Agent user só em tenants do programa Frontier preview, no contexto Agent 365. https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity

## Estado

Proposto. A validação real fica para as fases C e D.
