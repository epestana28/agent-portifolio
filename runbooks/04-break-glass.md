# 04. Break-glass: contenção com a aplicação indisponível

Estado deste runbook: escrito a partir da documentação oficial, sem tenant. Nunca executado. Até ser ensaiado nas fases C e D com a aplicação desligada, o teste BT-20 não está provado.

## Objectivo

Conter um agente directamente nos serviços Microsoft, sem usar a Control API, a base de dados, a fila nem qualquer componente desta aplicação.

Este runbook é independente da aplicação de propósito. Se a Control API, o SQL, o APIM, o Service Bus ou o Entra da própria aplicação falharem, o procedimento continua a funcionar. Só depende de contas e papéis Microsoft que existem fora da aplicação.

Ordem de acção, da mais específica para a mais ampla: desactivar a identidade do agente, remover o grant no recurso, parar o runtime. Políticas de âmbito tenant só em último caso.

## Quem pode executar

Pessoas com papéis Microsoft atribuídos fora desta aplicação e aprovados pela organização. Segundo a documentação:

| Acção | Papel que a documentação indica |
| --- | --- |
| Desactivar ou reactivar uma agent identity | Agent ID Administrator, ou Cloud Application Administrator. Os donos da identidade podem gerir os seus próprios agentes. |
| Desactivar o sign-in de um service principal | Cloud Application Administrator, Application Administrator, ou dono do service principal. |
| Desactivar um agent user | Permissão `User.EnableDisableAccount.All` com `User.Read.All` (Graph). O papel administrativo não vem indicado na página. |
| Remover um grant Selected no site | Aplicação com `Sites.FullControl.All`. Delegado não é suportado nesta operação. |
| Remover uma atribuição de papel de aplicação no Exchange | Membro do role group Organization Management, e Exchange Administrator no Entra. |
| Parar ou isolar o runtime | Papel Azure sobre o runtime. Não verificado. Confirmar na documentação actual. |

Contas e sessões de break-glass têm de ser acessíveis sem depender desta aplicação: não guardar as credenciais no Key Vault da aplicação, nem exigir o gateway, nem a identidade de controlo.

## Pré-requisitos

1. Decisão de contenção tomada por quem tem autoridade, e ticket (mesmo que aberto depois, em incidente).
2. Identificadores do agente, recolhidos antes do incidente e guardados fora da aplicação: `object_id` da identidade, `appId`, tipo de identidade (Entra Agent ID, managed identity ou service principal), blueprint, conta de utilizador associada, recursos e grants (com `permissionId` quando for Selected), e runtime. Se a base de dados estiver acessível, o registo `ExternalGrant` tem estes dados. Se não estiver, usar a cópia fora da aplicação.
3. Uma pessoa com acesso a esta lista e outra que conheça este runbook.
4. Uma conta com os papéis da tabela acima, testada nos últimos ensaios.
5. Saber o âmbito: um agente. Desactivar um blueprint ou o tenant exige âmbito visível, impacto e aprovação distintos.

## Passos

Fazer os passos pela ordem. Registar cada acção e hora antes de passar à seguinte.

### A. Desactivar a identidade do agente

1. Identificar o tipo da identidade.
2. Entra Agent ID (agent identity): no centro de administração do Entra, desactivar a agent identity específica, não o blueprint. A documentação diz que isto bloqueia o acesso e a emissão de tokens desse agente e que se pode reverter. Páginas: https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities e https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
3. Alternativa por API: `PATCH /servicePrincipals/{id}/microsoft.graph.agentIdentity`, permissão `AgentIdentity.EnableDisable.All`, Graph v1.0. O corpo com `accountEnabled` falso é inferência: nenhuma página de referência mostra o exemplo. Confirmar na documentação actual antes de executar. Página: https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0
4. Se o agente tem conta de utilizador associada (agent user): desactivar também, e revogar as sessões. Páginas: https://learn.microsoft.com/en-us/graph/api/agentuser-update?view=graph-rest-1.0 e https://learn.microsoft.com/en-us/graph/api/user-revokesigninsessions?view=graph-rest-1.0 (`POST /users/{id}/revokeSignInSessions`, permissão `User.RevokeSessions.All`). A aplicação da revogação de sessões a agent users é inferência. A desactivação em cascata do agent user a partir da agent identity não está verificada: tratar como dois passos.
5. Service principal de aplicação: `PATCH https://graph.microsoft.com/v1.0/servicePrincipals/{id}` com `{"accountEnabled": false}`, permissão `Application.ReadWrite.All`. A documentação diz que isto impede a emissão de tokens nesse tenant. Página: https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/disable-user-sign-in-portal
6. Managed identity: não existe procedimento verificado de desactivação por `accountEnabled`. Não eliminar a identidade como primeira acção. Ir aos passos B e C. Confirmar na documentação actual se há mecanismo de desactivação. Página sobre limites: https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq
7. Agentes com Agent 365: se a organização usa o bloqueio do centro de administração do Microsoft 365, usá-lo também. Se altera o estado no Entra: não verificado. Fazer ambos os bloqueios.
8. Não aplicar nesta fase as três políticas de Conditional Access de âmbito tenant, nem desactivar um blueprint, sem aprovação distinta. A documentação diz que as políticas não impedem a criação de identidades, e que os requisitos de licença se contradizem entre páginas. Fica como último recurso, com análise de impacto.

### B. Remover o grant no recurso

1. SharePoint, grant Selected ao nível do site: `DELETE /sites/{sitesId}/permissions/{permissionId}`, Graph v1.0, só aplicação, `Sites.FullControl.All`. Resposta esperada: 204 sem corpo. Página: https://learn.microsoft.com/graph/api/site-delete-permission?view=graph-rest-1.0
2. Lista ou item: a documentação encontrada só mostra a versão beta, e o caminho do item parece incompleto. Confirmar na documentação actual antes de executar. Página: https://learn.microsoft.com/graph/api/list-delete-permissions?view=graph-rest-beta
3. Se não se conhece o `permissionId` e a aplicação está em baixo: a listagem de permissões não foi verificada. Alternativa documentada: revogar o consentimento da aplicação no Entra, que bloqueia o acesso a qualquer lista que tivesse grant. O papel administrativo para o consentimento não foi verificado. Página: https://learn.microsoft.com/en-us/graph/permissions-selected-overview
4. Atenção: remover um consentimento de nível superior (`Sites.*`) não retira um acesso dado por um nível inferior (`Lists.*`). Rever os níveis.
5. Exchange (fase posterior): `Remove-ManagementRoleAssignment` e, se existirem permissões Entra amplas, retirá-las também, porque os grants somam-se. Página: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
6. Azure RBAC: retirar as atribuições de papel da identidade nos recursos. A documentação diz que alguns sistemas, como o Azure RBAC, continuam a aceitar o token até expirar. Página: https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq

### C. Parar o runtime

1. Parar ou isolar o runtime onde o agente executa (Azure Container Apps no piloto). O comando concreto não foi verificado nesta pesquisa: confirmar na documentação actual antes de executar.
2. O objectivo é o agente deixar de correr, para que deixe de usar a credencial que já tem. Inferência, por validar em ensaio: parar o processo impede que ele continue a usar tokens que tenha em memória, o que as acções dos passos A e B não fazem.
3. Não parar a Control API nem o executor nesta fase se não for necessário: o objectivo é conter o agente, e a aplicação pode voltar a ser útil mais tarde para reconciliar. Só o fazer por decisão expressa.
4. Se a decisão for fechar o tráfego do agente no gateway, fazê-lo no APIM pelo método aprovado. O comando não foi verificado.

### D. Verificar

1. Registos de início de sessão do Entra para a identidade do agente: depois da hora da desactivação, não deve haver emissão de tokens novos. A documentação mostra consulta em Graph beta a `auditLogs/signIns` com filtro por tipo de agente. Confirmar o pedido exacto na página de gestão. Página: https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
2. Em ambiente de teste: pedido de teste ao recurso com a identidade do agente (ou com um token novo para a mesma) deve falhar.
3. Um token emitido antes da desactivação pode continuar válido até expirar. Medir e registar o intervalo real em ensaio (teste BT-14). Pior caso documentado para access token: 60 a 90 minutos por omissão, cache de managed identity de cerca de 24 horas.
4. Registar o que ficou por verificar como `desconhecido`.

### E. Reconciliar quando a aplicação voltar

1. Suspender o agente na Control API (runbook 01) para o estado local coincidir com a contenção feita à mão.
2. Registar na auditoria, por pessoa autorizada, o que foi feito fora da aplicação: acção, serviço, hora, quem. A aplicação não sabe o que se fez sem esse registo.
3. Reconciliar os grants observados com os registados.
4. Reactivar só pelo runbook 03.

## Ensaio periódico

O break-glass só conta se for ensaiado. O ensaio é a evidência do teste BT-20 e só se faz nas fases C e D, com recursos de teste.

1. Periodicidade: definida e aprovada pela organização. Este runbook não fixa valor. Repetir também depois de qualquer alteração de papéis, de tenant ou do runtime.
2. Quem ensaia: uma pessoa independente de quem escreveu o runbook, a partir do documento, sem ajuda.
3. Como: com um agente de teste e um recurso de teste, desligar a aplicação (control-api, executor e worker, ou a parte que a organização aprovar) e executar os passos A a D. Cronometrar cada passo.
4. O que registar: tempo de cada passo, papéis usados, erros, passos que o texto não permitiu seguir, e se o recurso de teste ficou inacessível ao agente. Registar o tempo até à recusa efectiva e o comportamento de tokens anteriores.
5. Depois do ensaio: reactivar o agente de teste pelo runbook 03 e corrigir este runbook com o que falhou.
6. Verificar também que as contas de break-glass entram e têm os papéis certos. Um ensaio que falha por falta de acesso é um resultado útil.

## Como verificar (resumo)

1. A identidade do agente está desactivada, ou o acesso removido, segundo o tipo.
2. O grant do recurso foi removido e um pedido de teste falha.
3. O runtime está parado.
4. Os registos de início de sessão não mostram emissão nova.
5. O que não foi possível verificar ficou escrito como desconhecido.

## Evidência a registar

- Quem decidiu, quem executou, ticket, horas UTC de cada acção.
- Identificadores da identidade, do grant e do recurso afectados.
- Pedido e resposta de cada acção, sem tokens, segredos nem conteúdo de documentos. Para acções no centro de administração, uma nota do que foi feito e a hora.
- Resultado das verificações e o que ficou desconhecido.
- No ensaio: tempos, falhas do texto e correcções.

## Limites conhecidos

- Desactivar a identidade e bloquear a emissão não garantem invalidar os access tokens já emitidos. A documentação de Agent ID não diz o que acontece aos tokens já emitidos a uma agent identity desactivada.
- CAE não é garantia universal. Cobre eventos sobre utilizadores e, para workload identities, só o Microsoft Graph, service principals single-tenant e clientes que declaram suporte. Managed identities não são suportadas. Se uma agent identity é abrangida: não verificado.
- Managed identity: não se força a renovação de token, a cache ronda 24 horas, e as alterações de permissões podem demorar horas a fazer efeito. Eliminar a identidade não invalida tokens já emitidos até à expiração original.
- URL pré-autenticado de descarga: pode continuar válido até uma hora depois de retirado o acesso.
- Exchange: 30 minutos a 2 horas de propagação, e o cmdlet de teste ignora a cache.
- Conditional Access não cobre managed identities, nem o blueprint a criar identidades, nem o acesso por chave de API fora do Entra.
- Agentes antigos do Copilot Studio ou Foundry podem existir como service principals sem agent identity. Não se aplicam os passos de agent identity a esses.
- Se um passo mostra "confirmar na documentação actual", não executar a partir deste texto sem o confirmar.
- Não se desactiva em cascata o que não foi pedido: desactivar uma agent identity não deve afectar identidades irmãs do blueprint (BT-13), e isto só se confirma em tenant.
- Dados já exfiltrados e operações já aceites por serviços externos não se anulam.

## Documentação oficial (verificada a 2026-11-11)

- Desactivar identidades de agente: https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities
- Gerir identidades de agente: https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
- Graph, actualizar agent identity: https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0
- Graph, actualizar agent user: https://learn.microsoft.com/en-us/graph/api/agentuser-update?view=graph-rest-1.0
- Graph, revogar sessões: https://learn.microsoft.com/en-us/graph/api/user-revokesigninsessions?view=graph-rest-1.0
- Desactivar sign-in de service principal: https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/disable-user-sign-in-portal
- Managed identities, perguntas frequentes: https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq
- Selected permissions: https://learn.microsoft.com/en-us/graph/permissions-selected-overview
- Exchange, RBAC for Applications: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
- Revogar acesso: https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access
