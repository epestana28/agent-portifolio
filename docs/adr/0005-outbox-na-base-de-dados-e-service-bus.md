# ADR 0005: Outbox na base de dados e Service Bus

Estado: proposto. Data de verificação das fontes: 2026-11-11.

## Contexto

Uma suspensão tem de mudar o estado e, por fim, accionar acções externas (desactivar identidade, remover grants). Se a base de dados e o transporte de mensagens forem escritos em separado, uma falha entre os dois perde a contenção externa ou cria uma mensagem sem estado que a justifique.

Estado real: a fatia local grava a `ContainmentOperation`, a mensagem de outbox e o evento de auditoria na mesma transacção SQL. O `ContainmentWorker` lê a outbox da base de dados. Não existe Service Bus real. O Service Bus é o transporte alvo e é um pressuposto de desenho, não uma coisa implementada (pressuposto P-18).

## Decisão

1. Padrão outbox transaccional: a mensagem de contenção é escrita na mesma transacção que muda o estado do agente e incrementa a `revocation_epoch`. Não há escrita dupla.
2. Cada mensagem leva o tenant, o `SubjectId` (o identificador da `ContainmentOperation`) e a epoch com que nasceu. O agente obtém-se da operação.
3. O consumidor descarta uma mensagem de epoch anterior à actual como `superseded` (evento `containment.superseded`). O worker nunca reactiva um agente. Só a reactivação aprovada por um segundo PolicyAdmin o faz.
4. Consumidores idempotentes. A entrega é pelo menos uma vez, por isso um passo repetido não pode alterar o resultado nem duplicar eventos de ledger.
5. Em produção, o transporte é o Azure Service Bus. A deduplicação do Service Bus só actua no envio, numa janela limitada (omissão de 10 minutos). Não protege contra reentrega ao consumidor.
6. Mensagens em dead-letter geram alerta e entram num procedimento de tratamento. A dead-letter queue não se limpa sozinha.
7. Mensagens fora de ordem são esperadas. A ordem não é garantida pelo desenho. Quem decide é a epoch e o estado em SQL, não a posição na fila.
8. Nenhum passo externo conta como feito sem sonda de verificação (ADR 0008).

## Consequências

- Há atraso entre o commit e o envio. O bloqueio local não depende dele: o estado `Suspended` e a nova epoch já recusam admissões no momento do commit.
- É preciso um processo que leia a outbox e a envie, com tratamento de falhas e de reenvio. Esse processo é código próprio a testar.
- O número de sessões e o suporte a deduplicação por escalão do Service Bus são NÃO VERIFICADOS. A escolha de escalão fica por validar.
- Na fase B só se prova a lógica local. O comportamento com um Service Bus real (reentrega, dead-letter, fora de ordem) fica para a fase C e D.
- A tabela de outbox cresce. É preciso política de arquivo que não toque na ledger (ADR 0004).

## Alternativas rejeitadas

- Escrita dupla (base de dados e bus sem outbox): uma falha entre as duas escritas perde ou inventa contenção.
- Só o bus, sem estado em SQL: a decisão de admissão precisa do estado autoritativo, e uma mensagem perdida não deixa rasto.
- Confiar na deduplicação do Service Bus: só cobre o envio e uma janela curta.
- Confiar na ordem das mensagens: o desenho tem de aguentar mensagens fora de ordem e duplicadas.
- Reactivação automática pelo worker quando a mensagem de contenção falha: contradiz a regra de que a reactivação exige aprovação humana.

## Evidência documental

Todas verificadas em 2026-11-11. As páginas do Service Bus foram lidas por excerto de pesquisa.

- Entrega pelo menos uma vez e deduplicação: a detecção de duplicados actua no envio, com janela de 10 minutos por omissão. https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection
- Dead-letter queue: as mensagens não são apagadas automaticamente. https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues
- Modos de recepção e liquidação de mensagens (peek-lock). https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-transfers-locks-settlement
- Sessões e ordem: o suporte por escalão fica NÃO VERIFICADO. https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sessions
- O padrão outbox em si é inferência de desenho, não está numa página Microsoft lida.

## Estado

Proposto. A confirmação do transporte alvo é pressuposto P-14 na fase A.
