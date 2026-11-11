# ADR 0008: Estados de contenção separados

Estado: proposto. Data de verificação das fontes: 2026-11-11.

## Contexto

"Suspender um agente" mistura três coisas: a decisão do produto, as acções feitas fora (Entra, grants, runtime) e a prova de que essas acções tiveram efeito. Um único estado esconde as diferenças. Um agente marcado como "suspenso" pode ainda ter um token válido, um grant directo ou uma managed identity com cache.

A documentação Microsoft mostra vários casos em que a acção administrativa e o efeito real divergem. A cache do RBAC for Applications do Exchange é contornada pelo cmdlet de teste. A URL de transferência do SharePoint mantém validade depois da revogação. Um 200 não prova que a operação teve o efeito pretendido.

Estado real: o código tem `AgentState`, `ContainmentState` e `StepStatus` separados. A agregação está na fatia local e é testada com adaptadores simulados. Nenhuma sonda correu contra serviços reais.

## Decisão

Três máquinas de estados separadas.

| Máquina | Valores |
|---|---|
| `AgentState` | Draft, PendingReview, Active, Suspended, Retired |
| `ContainmentState` | Requested, LocalBlocked, ExternalPending, VerifiedScoped, Partial, Failed, Unknown |
| `StepStatus` | Requested, Executed, Verified, Failed, Unknown |

Regras de agregação do `ContainmentState`:

1. `VerifiedScoped` só existe se todos os passos estiverem `Verified` por sonda, com o âmbito da verificação explícito.
2. Qualquer passo `Unknown` torna o conjunto `Unknown`.
3. Uma mistura de passos verificados e falhados dá `Partial`.
4. Todos os passos falhados dão `Failed`.
5. `Suspended` nunca passa a `VerifiedScoped` por si. São máquinas diferentes.

Outras regras:

- A suspensão faz uma única transacção: `Suspended`, epoch incrementada, `ContainmentOperation` em `LocalBlocked`, `InFlightActionIds`, outbox e auditoria.
- A confirmação administrativa (a chamada devolveu sucesso) é distinta da confirmação por sonda (o efeito foi observado). Só a segunda leva a `Verified`.
- Cada passo e cada limitação permanente (`StandingLimitation`) vêm no relatório. Por exemplo, um token já emitido que continua válido, ou uma managed identity sem disable.
- Uma sonda que não consegue concluir dá `Unknown`, não `Failed` nem `Verified`.
- A reactivação é um fluxo próprio: pedida por um PolicyAdmin, aprovada por outro, com grants reverificados, nova epoch e nova `policy_version`.

## Consequências

- O relatório de contenção mostra honestamente o que se sabe e o que não se sabe. `VerifiedScoped` é raro e tem valor.
- É preciso desenhar uma sonda por passo e por tipo de identidade. Algumas não existem na documentação (por exemplo, sonda de emissão de token após disable). Ficam como lacunas e bloqueiam o marco D.
- O operador vê `Partial` ou `Unknown` com frequência. A interface e o runbook têm de explicar o que fazer.
- Os estados `Unknown` ficam abertos até uma sonda posterior os resolver. É preciso reprocessamento e prazos.
- Mais estados implicam mais testes de transição e de concorrência (suspensão e reactivação em simultâneo).

## Alternativas rejeitadas

- Um só estado `Suspended` com um booleano de "contido": esconde `Partial` e `Unknown`.
- Marcar `VerifiedScoped` quando a chamada à API devolve sucesso: confunde acção com efeito. A documentação mostra casos em que diferem.
- Tratar `Unknown` como `Failed`: leva a repetir acções externas que talvez já tenham sido feitas. Tratá-lo como `Verified` seria pior.
- Reactivar automaticamente quando a contenção falha: contradiz a regra de aprovação humana por um segundo PolicyAdmin.
- Esperar pela confirmação externa antes de bloquear localmente: o bloqueio local é imediato e não depende de nenhum serviço externo.

## Evidência documental

Todas verificadas em 2026-11-11.

- O cmdlet `Test-ServicePrincipalAuthorization` contorna a cache do RBAC for Applications, e a propagação da cache demora de 30 minutos a 2 horas. Leitura por excerto. https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
- A URL de transferência pré-autenticada do SharePoint mantém validade depois de o acesso ser retirado. https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0
- "A 200 OK isn't proof of ingestion". Frase de uma página sobre ingestão de eventos, usada aqui por analogia. A aplicação ao caso é inferência. https://learn.microsoft.com/en-us/azure/azure-monitor/logs/logs-ingestion-api-overview
- Tokens já emitidos após desactivação de agent identity: NÃO VERIFICADO. https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
- Managed identity sem disable documentado e com cache de tokens de cerca de 24 horas. https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq
- O desenho das três máquinas é decisão do projecto e não vem de página Microsoft.

## Estado

Proposto. O fecho depende de sondas reais nas fases C e D.
