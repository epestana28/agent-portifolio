# Runbooks operacionais

Procedimentos para uma pessoa independente seguir. Cada runbook tem objectivo, quem pode executar, pré-requisitos, passos numerados, como verificar, evidência a registar e limites conhecidos.

## Estado real

Fase A (documentos de validação): escrita a partir de documentação oficial, sem tenant. Fase B (fatia vertical local): implementada em .NET 8, sem credenciais reais, com o mundo Microsoft simulado. Fases C, D e E: não executadas. O Bicep está escrito e nunca foi aplicado. Não existe adaptador real para Entra nem para Graph.

Por isso, nenhum destes runbooks foi executado contra serviços Microsoft reais. Os passos Microsoft descrevem a acção e apontam a página oficial, e usam só endpoints e cmdlets que a pesquisa verificou em Microsoft Learn a 2026-11-11. Onde a pesquisa não verificou, o texto diz "confirmar na documentação actual antes de executar". Não executar esses passos sem o confirmar.

Os passos que dizem "inferência" são conclusões a partir de factos verificados e não estão escritos na documentação. Os que dizem "por validar" dependem do tenant.

## Índice

| Runbook | Quando usar | Papel |
| --- | --- | --- |
| [01. Suspender um agente](01-suspender-agente.md) | Incidente com um agente. Bloqueio local imediato e acompanhamento da contenção externa. | `IncidentOperator` |
| [02. Revogar uma capacidade](02-revogar-capacidade.md) | Retirar uma capacidade de um agente sem o suspender. | `IncidentOperator` ou `PolicyAdmin` |
| [03. Reactivar um agente](03-reactivar-agente.md) | Depois de uma suspensão tratada. Dois `PolicyAdmin` distintos, grants reverificados, capacidades reemitidas. | Dois `PolicyAdmin` |
| [04. Break-glass](04-break-glass.md) | A aplicação está indisponível. Contenção directa nos serviços Microsoft, independente da aplicação. Ensaio periódico. | Administradores Microsoft fora da aplicação |
| [05. Verificar a ledger e os digests](05-verificar-ledger-e-digests.md) | Verificação periódica, depois de restauro, ou suspeita de adulteração. | `Auditor` |
| [06. Restauro e failover](06-restauro-e-failover.md) | Depois de restauro ou failover da base de dados. Não reabrir tráfego antes de reconciliar. | Operações, `PolicyAdmin`, `Auditor` |
| [07. Repetir os testes de contenção](07-repetir-testes-de-contencao.md) | Repetir localmente os testes BT-01 a BT-20 e saber quais só se provam nas fases C e D. | Engenheiro |

## Como escolher

1. A Control API responde? Se sim, runbook 01 para suspender. Se não, runbook 04.
2. Só uma capacidade tem de sair, sem suspeita de comprometimento? Runbook 02.
3. Voltar a pôr um agente em serviço? Runbook 03, sempre.
4. Houve restauro ou failover? Runbook 06, e dentro dele o 05 antes de qualquer outra coisa.
5. Suspeita de adulteração da auditoria? Runbook 05.

## Regras comuns

- Nunca registar tokens, segredos, prompts nem conteúdo de documentos em tickets, notas ou evidência.
- Suspender não é conter. O estado do agente e o estado da contenção são separados. `Suspended` nunca passa sozinho a `VerifiedScoped`.
- `VerifiedScoped` só vale para o âmbito explícito de cada passo. Nunca dizer "acesso universal revogado".
- Desactivar uma identidade ou retirar um grant não garante invalidar tokens já emitidos. A documentação não dá essa garantia, e para agentes não diz o que acontece.
- Um passo em estado desconhecido é desconhecido, não é sucesso.
- O worker de contenção nunca reactiva um agente. A reactivação só se faz pelo runbook 03.
- Acções já admitidas ou enviadas ao Microsoft Graph não se cancelam. Ficam registadas à parte, com o resultado que tiverem, incluindo desconhecido.
- Antes de executar um passo de um serviço Microsoft marcado como "confirmar na documentação actual", confirmar.

## Documentos relacionados

- Infraestrutura (Bicep, nunca aplicado): `../infra/bicep/README.md`
- Especificação de origem: a especificação do projecto, secção 10 (suspensão e revogação), secção 11 (ledger) e secção 15 (testes bloqueantes)
