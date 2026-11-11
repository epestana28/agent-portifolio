# 01. Suspender um agente

Estado deste runbook: escrito a partir do desenho e da documentação oficial, sem tenant. Nas fases A e B os passos externos de contenção são simulados. Os passos que tocam em serviços Microsoft reais nunca foram executados.

## Objectivo

Bloquear de imediato novas acções de um agente através da Control API e acompanhar a contenção externa até ao fim, com cada passo classificado como solicitado, executado, verificado, falhado ou desconhecido.

A suspensão não prova que o agente deixou de ter acesso. Prova que a aplicação deixou de admitir novas acções dele. O acesso externo só se dá por contido quando os passos externos estão verificados por sonda.

## Quem pode executar

Papel `AgentPortifolio.IncidentOperator` ou `AgentPortifolio.PolicyAdmin`. Os dois são aceites pelo código (`ControlService.SuspendAsync`). Um operador sem um destes papéis recebe 403 e não consegue suspender (teste `BT19_`).

## Pré-requisitos

1. Identificador interno do agente (`{id}`) e tenant. Um UUID não é autorização: confirmar com o responsável do agente que é o agente certo.
2. Motivo e, se existir, número de ticket.
3. Token Entra para a audiência da Control API (`Auth__Audience` da control-api), obtido pelo método aprovado na organização. Nunca colar o token em tickets, mensagens ou notas.
4. URL base da Control API (`$CONTROL_API`).
5. Se a Control API estiver indisponível, não insistir: passar ao runbook 04 (break-glass).

## Passos

1. Confirmar o agente e o motivo com uma segunda pessoa sempre que o tempo o permitir. Em incidente activo, suspender primeiro e confirmar depois, porque a suspensão é reversível pelo runbook 03.
2. Enviar o pedido de suspensão:

   ```bash
   curl -sS -X POST "$CONTROL_API/agents/$AGENT_ID/suspend" \
     -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"reason":"TEXTO CURTO DO MOTIVO"}'
   ```

   O corpo é `{"reason":"..."}` e o motivo é obrigatório: um motivo vazio dá 400 (`invalid_request`). Um campo desconhecido no corpo também dá 400. Se o agente já estiver suspenso, o pedido devolve a operação aberta para a epoch actual e não sobe a epoch outra vez. Se o agente estiver noutro estado que não `Active`, a resposta é 409 `not_suspendable`.
3. Ler o código HTTP e o corpo. Uma resposta de sucesso significa que o bloqueio local ficou persistido: uma única transacção suspendeu o agente, incrementou o `revocationEpoch`, criou a operação de contenção em `LocalBlocked`, listou as acções em curso e escreveu a outbox e a auditoria.
4. Se a resposta se perdeu (tempo esgotado, erro de rede), não repetir às cegas e não assumir nada. Ir ao passo seguinte: a existência de uma operação de contenção para o agente mostra que a suspensão ficou persistida. Se a leitura também falhar, tratar a aplicação como indisponível e passar ao runbook 04.
5. Ler as contenções do agente:

   ```bash
   curl -sS "$CONTROL_API/agents/$AGENT_ID/containments" \
     -H "Authorization: Bearer $TOKEN"
   ```

6. Interpretar o estado global da operação mais recente com a tabela abaixo. Repetir a leitura com intervalos curtos até o estado deixar de mudar ou até decidir escalar. Não há prazo universal presumido: o tempo depende da identidade e do recurso.
7. Interpretar cada passo (tabela abaixo). Ler a `limitation` de cada um: é o texto que diz o que o passo não cobre.
8. Ler a lista de acções em curso (`InFlightActionIds`). São as acções admitidas ou em execução no momento da suspensão. Ficam fora da garantia de bloqueio e são tratadas à parte (secção Limites).
9. Se algum passo externo ficar em `Failed` ou `Unknown`, ou o estado global não chegar a `VerifiedScoped`, não repetir o pedido de suspensão. Decidir com o responsável: esperar pela retentativa do worker, ou executar o passo em falta à mão pelo runbook 04, só para esse passo.
10. Registar a decisão e informar o responsável do agente. Para reactivar, seguir o runbook 03. O worker nunca reactiva um agente.

### Estados globais da contenção

| Estado | Significado | O que fazer |
| --- | --- | --- |
| `Requested` | Existe no tipo, mas o código cria a operação directamente em `LocalBlocked`, na mesma transacção da suspensão. Não se espera ver este estado. | Se aparecer, tratar como falha e usar o runbook 04. |
| `LocalBlocked` | Bloqueio local persistido. Novas acções do agente são negadas pela aplicação. Nenhum passo externo está confirmado. | Estado seguro para a aplicação, não para o recurso. Acompanhar os passos externos. |
| `ExternalPending` | Há passos externos em curso. | Acompanhar. Não declarar contido. |
| `VerifiedScoped` | Todos os passos foram verificados por sonda, para o âmbito explícito de cada um. | Contido no âmbito listado, e só nele. Nunca dizer "acesso universal revogado". |
| `Partial` | Mistura de passos verificados e não verificados ou falhados. | Listar os passos em falta. Tratar à mão (runbook 04) os que o exijam. |
| `Failed` | Todos os passos externos falharam. | Executar a contenção externa à mão (runbook 04) e abrir incidente. |
| `Unknown` | Pelo menos um passo ficou sem confirmação. | Qualquer passo `Unknown` torna o estado global `Unknown`. Não tratar como sucesso. Verificar à mão. |

`Suspended` nunca passa sozinho a `VerifiedScoped`. O estado do agente e o estado da contenção são separados de propósito.

### Estados por passo

| Estado do passo | Significado | Cuidado |
| --- | --- | --- |
| `Requested` | O passo foi pedido e ainda não correu. | Sem efeito confirmado. |
| `Executed` | O serviço Microsoft aceitou a acção (confirmação administrativa). | Não é prova de efeito. Falta a sonda. |
| `Verified` | Uma sonda confirmou o efeito, para o âmbito indicado. | O âmbito está na `limitation`. Não generalizar. |
| `Failed` | O passo falhou. | Ler a evidência. Throttling (429 ou 503) esgotadas as retentativas pode aparecer aqui ou como estado parcial visível. |
| `Unknown` | Tempo esgotado ou resposta sem confirmação. | Reconciliar. Não assumir sucesso nem falha. |

Os campos JSON são os do modelo (`src/Core/Domain/Model.cs`), em camelCase e com os estados em texto: `state`, `scope`, `epochAfter`, `steps` (`name`, `resource`, `status`, `limitation`, `attempts`, `startedAt`, `lastCheckedAt`, `evidence`), `inFlightActionIds` e `uncertainties`. Os nomes dos passos hoje são `identity.disable`, `grants.remove`, `runtime.isolate` e `tokens.probe`, todos simulados. O passo `identity.disable` não se aplica a managed identities.

## Como verificar

1. O estado do agente é `Suspended` e a operação de contenção existe, com a `revocationEpoch` incrementada.
2. Uma tentativa de acção do agente é recusada com `agent_suspended` (ou `capability_stale_epoch` para capacidades anteriores à suspensão). Em ambiente de teste, fazer a tentativa com um agente de ensaio. Em produção, não provocar acções do agente real: ler a auditoria (evento `action.denied` com o motivo, e `alert.post_suspension_attempt` se o agente tentou).
3. Cada passo externo mostra o estado `Verified` e o âmbito. Qualquer coisa abaixo disso é lacuna, não sucesso.
4. A lista de acções em curso foi lida e cada acção tem destino conhecido (`Succeeded`, `Failed`, `Unknown`). As `Unknown` ficam registadas para reconciliação.
5. Prova independente do lado Microsoft, quando a contenção externa for real: os registos de início de sessão do Entra para a identidade do agente (a documentação mostra consulta em Graph beta a `auditLogs/signIns`, ver página de gestão) e a recusa de um pedido de teste ao recurso. Confirmar na documentação actual antes de executar.

## Evidência a registar

- Identificador do agente, tenant, motivo, ticket, quem executou e a que horas (UTC).
- Identificador da operação de contenção, valor de `revocationEpoch` depois da suspensão.
- Estado global e estado de cada passo, com a `limitation` e a hora da última verificação, em cada leitura.
- Lista de acções em curso e destino de cada uma.
- Eventos de auditoria relevantes (suspensão, passos, recusas pós-suspensão).
- Decisões tomadas (esperar, tratar à mão) e quem as aprovou.

Não registar tokens, segredos, conteúdo de documentos nem prompts.

## Limites conhecidos

- Acções já admitidas e em curso não são interrompidas pela suspensão. A revalidação imediatamente antes da execução apanha as que ainda não começaram. Uma acção já enviada ao Microsoft Graph pode completar-se. Fica registada como `Unknown` se não houver confirmação. Não há rollback universal.
- Desactivar uma identidade ou bloquear a emissão não invalida os access tokens já emitidos. A duração por omissão de um access token é de 60 a 90 minutos segundo uma página e de 1 hora segundo outra. Para managed identities a cache de tokens ronda 24 horas e não é possível forçar a renovação. Para tokens de agentes específicos, a documentação não diz o que acontece.
- CAE não é garantia universal. A documentação de CAE cobre utilizadores e, para workload identities, só pedidos ao Microsoft Graph e service principals single-tenant, e exclui managed identities. Se uma agent identity é abrangida: não verificado.
- Um URL pré-autenticado de descarga já emitido pode continuar válido até uma hora (pior caso documentado).
- No Exchange, a revogação de uma atribuição de papel de aplicação demora entre 30 minutos e 2 horas a fazer efeito. O cmdlet de teste ignora a cache e não serve de prova.
- Existem dois interruptores independentes para agentes com Agent 365: o Entra e o centro de administração do Microsoft 365. Se bloquear num altera o outro: não verificado.
- Os testes BT-05, BT-06, BT-14, BT-17 e BT-20 só se provam nas fases C e D.

## Documentação oficial (verificada a 2026-11-11)

- Desactivar identidades de agente: https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities
- Gerir identidades de agente (administração): https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
- Revogar acesso (utilizadores, tokens): https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access
- CAE: https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation
- CAE para workload identities: https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation-workload
