# 03. Reactivar um agente suspenso

Estado deste runbook: escrito a partir do desenho e da documentação oficial, sem tenant. Nunca executado contra serviços Microsoft reais.

## Objectivo

Voltar a activar um agente suspenso, com revisão reforçada: um `PolicyAdmin` pede, outro `PolicyAdmin` aprova, os grants externos são reverificados depois da suspensão, e o agente volta com nova epoch e nova `policyVersion`.

A reactivação não repõe nada do passado. As capacidades emitidas antes da suspensão morreram com o incremento do `revocationEpoch` e têm de ser reemitidas. Mensagens de contenção antigas nunca se reproduzem. O worker de contenção nunca reactiva um agente.

## Quem pode executar

Dois titulares distintos do papel `AgentPortifolio.PolicyAdmin`. Quem pede não pode aprovar. A reactivação e o aumento de privilégios exigem revisão reforçada, por desenho.

Para os passos no Microsoft Entra (passo 6), um administrador com o papel que a documentação indica para gerir identidades de agente: Agent ID Administrator, ou Cloud Application Administrator. Os donos da identidade de agente podem gerir os seus próprios agentes sem esses papéis.

## Pré-requisitos

1. Causa da suspensão compreendida e tratada, com ticket. Não reactivar para "ver se funciona".
2. Estado da contenção da suspensão lido (runbook 01). Nenhum passo externo por esclarecer: `Unknown` ou `Failed` resolvem-se antes.
3. Se o motivo foi comprometimento do agente ou da sua credencial: credenciais rodadas ou revogadas, e a revisão de segurança concluída.
4. Reverificação dos grants externos concluída depois da hora da suspensão (passo 3). Os grants anteriores à suspensão não contam.
5. Tokens Entra dos dois `PolicyAdmin` para a audiência da Control API, pelo método aprovado. Não os registar.
6. Se a identidade foi desactivada no Entra durante a contenção: saber que continua desactivada até alguém a reactivar (passo 6).

## Passos

1. Rever a causa e decidir o âmbito do regresso: as mesmas capacidades, menos capacidades, ou capacidades novas. Aumentar privilégios exige a mesma revisão reforçada e fica registado.
2. O primeiro `PolicyAdmin` regista o pedido:

   ```bash
   curl -sS -X POST "$CONTROL_API/agents/$AGENT_ID/reactivation-requests" \
     -H "Authorization: Bearer $TOKEN_ADMIN_1"
   ```

   Este pedido não tem corpo. O código não lê nenhum campo `reason` aqui: o motivo e o ticket registam-se no ticket e na evidência deste runbook, não na aplicação. O agente tem de estar `Suspended`, senão a resposta é 409 `invalid_state`. A aplicação regista quem pediu (evento `agent.reactivation_requested`).
3. Reverificar os grants externos do agente, depois da suspensão e antes da aprovação:
   - Inventariar o que o agente e a sua identidade de execução têm no Microsoft Entra e no recurso: consentimentos de aplicação, grants Selected no site ou lista, atribuições de papel Azure, e (fase posterior) atribuições no Exchange.
   - Comparar com o registo da aplicação. Qualquer grant que não esteja registado, ou que seja amplo (`Sites.Read.All`, `Files.Read.All`), é drift perigoso e impede a reactivação até ser retirado ou formalmente aceite.
   - Grants no próprio agente contam como caminho directo e baixam a cobertura (`DirectAccessPossible`).
   - Na aplicação, a reverificação é `POST /agents/{id}/reconcile` (papel `PolicyAdmin` ou `IncidentOperator`). Compara os grants observados com os registados e com as capacidades activas, e actualiza `lastVerifiedAt`. Um grant registado que já não aparece fica com resultado `absent`, e conta como reverificado.
   - Para Selected, a listagem de permissões de um site não foi verificada: confirmar na documentação actual antes de executar. Página: https://learn.microsoft.com/en-us/graph/permissions-selected-overview
   - Para Exchange, `Test-ServicePrincipalAuthorization -Identity <...> -Resource <caixa>` mostra o âmbito atribuído no Exchange, e exclui o que estiver concedido à parte no Entra. Um resultado `InScope False` não prova ausência de acesso se houver permissão no Entra. Página: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
4. O segundo `PolicyAdmin`, que não fez o pedido, revê o pedido, o resultado do passo 3 e a causa, e decide. Se a decisão for positiva:

   ```bash
   curl -sS -X POST "$CONTROL_API/agents/$AGENT_ID/reactivation-approvals" \
     -H "Authorization: Bearer $TOKEN_ADMIN_2"
   ```

   Também sem corpo. Respostas, tal como o código as dá: o mesmo titular a aprovar o seu próprio pedido recebe 403. Aprovar sem pedido prévio dá 409 `review_not_requested`. Se algum grant registado tiver `lastVerifiedAt` anterior ou igual à hora da suspensão, a resposta é 409 `grants_not_revalidated`. A reverificação faz-se com `POST /agents/{id}/reconcile` ou com `POST /agents/{id}/grants`, e hoje lê o simulador, não o tenant.
5. Ler o resultado. Em caso de sucesso o agente passa a `Active` com nova `revocationEpoch` e nova `policyVersion`. As capacidades anteriores continuam inválidas.
6. Se a identidade foi desactivada no Entra durante a contenção, reactivá-la agora, depois da aprovação:
   - Centro de administração do Entra: reactivar a agent identity. A documentação diz que reactivar uma identidade desactivada, em qualquer âmbito, repõe o acesso e a emissão de tokens. Página: https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
   - Por API: `PATCH /servicePrincipals/{id}/microsoft.graph.agentIdentity`, permissão `AgentIdentity.EnableDisable.All`, em Graph v1.0. O corpo (`accountEnabled` verdadeiro) é inferência: a página de referência não mostra um exemplo. Confirmar na documentação actual antes de executar. Página: https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0
   - Se o agente tem conta de utilizador associada (agent user), reactivar essa conta à parte: a política ou a desactivação de uma não está documentada como cobrindo a outra. Página: https://learn.microsoft.com/en-us/graph/api/agentuser-update?view=graph-rest-1.0
   - Não reactivar o blueprint nem o blueprint principal sem âmbito, impacto e aprovação distintos: afecta todas as identidades filhas.
   - Se o agente está bloqueado no centro de administração do Microsoft 365 (Agent 365), desbloquear também. Se esse bloqueio e o do Entra se sincronizam: não verificado.
   - Managed identities e service principals: se foram retirados grants, repor só os aprovados, pelos passos do runbook 04 invertidos e por pessoa autorizada. Se foi alterado `accountEnabled` de um service principal, página: https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/disable-user-sign-in-portal
7. Reemitir as capacidades aprovadas por `POST /agents/{id}/capabilities` (papel `PolicyAdmin`, corpo `action`, `resource`, `expiresAt`). O recurso tem de estar em forma canónica, a acção na lista fechada e a validade futura e de 90 dias no máximo (`ControlService.MaxCapabilityLifetime`). Cada capacidade nova fica com a nova epoch.
8. Executar os testes de regresso do passo "Como verificar" antes de devolver o agente ao responsável.

## Como verificar

1. O estado do agente é `Active`, com `revocationEpoch` e `policyVersion` superiores aos de antes da suspensão.
2. Uma acção coberta por uma capacidade reemitida, sobre o recurso autorizado, funciona (equivalente ao `BT01_`).
3. Uma acção sobre um recurso não autorizado falha (`BT01_`, parte negativa).
4. Uma capacidade da época anterior, se alguém a tiver guardado, é recusada com `capability_stale_epoch` (`BT03_`).
5. Nenhuma mensagem de contenção antiga foi reexecutada: a auditoria mostra `containment.superseded` para as de epoch diferente, e nenhuma reactivação automática (`BT10_` e, só como aproximação por reinício, `BT17_`; o restauro e o failover reais só se provam nas fases C e D).
6. O inventário de grants do passo 3 volta a ser igual ao registado, repetido depois da reactivação.
7. Vigiar o agente durante um período combinado: alertas de acesso directo, drift ou grant amplo.

## Evidência a registar

- Os dois `PolicyAdmin` (quem pediu, quem aprovou), horas UTC, ticket e causa.
- Resultado da reverificação de grants (inventário, comparação, diferenças encontradas e o que se fez).
- Pedido e aprovação, com os identificadores dos eventos de auditoria `agent.reactivation_requested` e `agent.reactivated`.
- Nova `revocationEpoch` e nova `policyVersion`.
- Capacidades reemitidas (identificador, acção, recurso canónico, validade).
- Resultado dos testes de regresso.
- Acções externas feitas no Entra ou no recurso, por quem e quando.

## Limites conhecidos

- Reactivar uma identidade no Entra repõe a emissão de tokens, mas a propagação não é imediata em todos os recursos. Para reactivação depois de desactivar utilizadores, a documentação de CAE indica atrasos típicos (cerca de 15 minutos no SharePoint Online e Teams, e 35 a 40 minutos no Exchange Online). Para agentes: não verificado.
- A reverificação de grants só vê o que a aplicação sabe consultar. Caminhos alternativos (chaves de API, segredos partilhados, acessos locais ao recurso) têm de ser inventariados à mão.
- A aprovação por duas pessoas não substitui a investigação da causa.
- A contenção anterior nunca foi "revertida" pela aplicação: o que foi alterado fora dela (identidade, grants, runtime) tem de ser reposto à mão e fica registado.
- A prova de regresso só é real com recursos reais (fases C e D).
