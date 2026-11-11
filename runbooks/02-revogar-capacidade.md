# 02. Revogar uma capacidade

Estado deste runbook: escrito a partir do desenho e da documentação oficial, sem tenant. Nunca executado contra serviços Microsoft reais.

## Objectivo

Retirar uma única capacidade interna de um agente (acção semântica sobre um recurso canónico), sem alterar as outras capacidades do mesmo agente e sem suspender o agente.

Uma capacidade é uma referência opaca interna, vinculada a tenant, agente, acção, recurso canónico, validade e epoch. Revogá-la não retira nenhum grant externo no Microsoft Entra ou no recurso. Se o acesso externo também tem de acabar, é um passo separado (passo 7).

## Quem pode executar

Papel `AgentPortifolio.IncidentOperator` ou `AgentPortifolio.PolicyAdmin`. Um operador sem um destes papéis recebe 403 (teste `BT19_`).

Se houver suspeita de comprometimento do agente, não revogar uma capacidade: suspender o agente (runbook 01).

## Pré-requisitos

1. Identificador do agente (`{id}`) e da capacidade (`{capabilityId}`). Obter a lista de capacidades do agente por `GET /agents/{id}`, que devolve o agente, as capacidades e os grants. Em alternativa, pela auditoria (`capability.granted`, papel `Auditor`).
2. Motivo e ticket.
3. Token Entra para a audiência da Control API, pelo método aprovado. Não o registar em lado nenhum.
4. Saber se o recurso dessa capacidade tem também um grant externo (registo `ExternalGrant`) que deva sair.

## Passos

1. Confirmar que a capacidade é a que se pretende: acção, recurso canónico e validade. Uma capacidade com o recurso errado revogada por engano obriga a reemitir a correcta.
2. Enviar o pedido:

   ```bash
   curl -sS -X DELETE "$CONTROL_API/agents/$AGENT_ID/capabilities/$CAPABILITY_ID" \
     -H "Authorization: Bearer $TOKEN"
   ```

3. Ler o código HTTP. Sucesso (200, com a capacidade e o campo `revokedAt`) significa que a capacidade ficou revogada na base de dados e que o evento `capability.revoked` foi escrito na auditoria. O pedido é idempotente: revogar uma capacidade já revogada devolve-a sem escrever outro evento. Um identificador que não pertença ao agente dá 404. Se o pedido falhar ou ficar sem resposta, repetir só depois de ler o estado da capacidade, para não confundir uma falha com um sucesso perdido.
4. Confirmar que o estado do agente não mudou (continua `Active`) e que o `revocationEpoch` não foi incrementado: revogar uma capacidade não é suspender. O que sobe é a `policyVersion`.
5. Em ambiente de teste, tentar uma acção coberta por essa capacidade com o agente de ensaio. Esperado: recusa com `capability_revoked`.
6. Tentar uma acção coberta por outra capacidade do mesmo agente. Esperado: continua a funcionar. É o que o teste `BT07_Revoking_one_capability_does_not_change_the_others` prova, só sobre o mundo simulado.
7. Se o grant externo também deve sair, tratá-lo à parte e com o mesmo cuidado:
   - Selected no SharePoint, ao nível do site: pedido `DELETE /sites/{sitesId}/permissions/{permissionId}` em Microsoft Graph v1.0, só em modo aplicação com `Sites.FullControl.All`. O modo delegado não é suportado nesta operação. Executar por identidade separada do executor e da control-api. Página: https://learn.microsoft.com/graph/api/site-delete-permission?view=graph-rest-1.0
   - Selected ao nível de lista ou item: a documentação encontrada só mostra a versão beta, e o caminho do item na página parece incompleto. Confirmar na documentação actual antes de executar.
   - O `permissionId` vem do registo `ExternalGrant.ExternalId` na base de dados (guardado quando o grant foi registado). O endpoint de listagem de permissões não foi verificado: confirmar na documentação actual.
   - Alternativa: revogar o consentimento no Entra, que bloqueia o acesso a qualquer lista com grant anterior. O papel administrativo necessário para o consentimento não foi verificado.
   - Exchange (fase posterior): `Remove-ManagementRoleAssignment`. Página: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
8. Depois de retirar o grant externo, reconciliar com `POST /agents/{id}/reconcile` (papel `PolicyAdmin` ou `IncidentOperator`): a leitura de grants observados e registados deve coincidir. Hoje a leitura é feita contra o simulador. Um grant amplo (por exemplo `Sites.Read.All`, `Files.Read.All`) é drift perigoso e suspende o agente.

## Como verificar

1. A auditoria tem o evento `capability.revoked` com o identificador da capacidade e o iniciador humano.
2. Acção sob a capacidade revogada: recusada com `capability_revoked`.
3. Acção sob outras capacidades do agente: continua a ser admitida.
4. Se o grant externo foi retirado: um pedido de teste ao recurso falha, e depois da propagação (que pode demorar, ver Limites) continua a falhar. Registar quanto tempo demorou a recusa efectiva.

## Evidência a registar

- Agente, capacidade (acção e recurso canónico), motivo, ticket, quem executou, hora UTC.
- Resposta da Control API e identificador do evento de auditoria.
- Resultado dos testes dos passos 5 e 6 (recusa e continuidade).
- Se aplicável: pedido e resposta do passo 7 (sem tokens), e o tempo até à recusa efectiva.

## Limites conhecidos

- A revogação interna só impede novas admissões. Uma acção já admitida e a executar não é cancelada. A revalidação antes da execução apanha as que ainda não começaram.
- Revogar a capacidade não retira o grant no recurso. Enquanto o grant externo existir, existe um caminho que contorna o gateway se alguém tiver a credencial do executor. Reduz-se a cobertura do agente (`DirectAccessPossible`) até o caminho ser eliminado.
- Em Selected, remover o grant da lista retira o acesso aos itens dela, independentemente de grants explícitos nos itens. Remover um consentimento de nível superior não retira um acesso dado por um nível inferior.
- No Exchange, a remoção demora entre 30 minutos e 2 horas a fazer efeito, e o cmdlet de teste ignora a cache.
- Um URL pré-autenticado de descarga já emitido pode continuar a funcionar até uma hora depois de retirado o acesso.
- Esta operação só se prova de ponta a ponta com recursos reais nas fases C e D.

## Documentação oficial (verificada a 2026-11-11)

- Selected permissions: https://learn.microsoft.com/en-us/graph/permissions-selected-overview
- Remover permissão de site: https://learn.microsoft.com/graph/api/site-delete-permission?view=graph-rest-1.0
- Exchange, RBAC for Applications: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
