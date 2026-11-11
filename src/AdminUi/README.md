# AdminUi

Esta pasta não tem código. A interface de administração está adiada. A interface é a Control API (`src/ControlApi`), usada por `curl` como nos runbooks.

## Porquê adiada

Uma interface gráfica só é segura se mostrar o que o sistema sabe e o que não sabe. Hoje o Microsoft é simulado e nenhuma contenção externa é real. Um ecrã verde por cima de uma simulação seria uma afirmação que o código não pode sustentar. A decisão é não esconder lacunas atrás de ecrãs: primeiro as provas das fases C e D, depois a interface.

Não há prazo atribuído. Está listada em `docs/architecture.md`, secção "O que não existe ainda".

## O que a interface teria de mostrar

Tudo vem da Control API. Nada disto exige dados novos.

1. Estado do agente e estado da contenção em separado. `Suspended` é o estado do agente. O estado da contenção (`LocalBlocked`, `ExternalPending`, `Partial`, `Failed`, `Unknown`, `VerifiedScoped`) é outra coisa e nunca se funde com o primeiro.
2. Cada passo de contenção com o seu estado (`Requested`, `Executed`, `Verified`, `Failed`, `Unknown`), a `limitation`, as tentativas, a hora da última verificação e a evidência. `Executed` não é `Verified`.
3. As incertezas (`uncertainties`) sempre visíveis, incluindo a que diz que tokens já emitidos podem continuar válidos até expirarem. Não podem estar escondidas num painel recolhido.
4. O âmbito (`scope`) do que ficou verificado. `VerifiedScoped` só aparece com o âmbito escrito ao lado, nunca como "acesso revogado".
5. As acções em curso no momento da suspensão (`inFlightActionIds`) e o destino de cada uma, com as `Unknown` em destaque.
6. A cobertura do agente (`GatewayEnforced` ou `DirectAccessPossible`) e as constatações da última reconciliação, com a hora. Sem reconciliação recente, dizer que não há dados.
7. Os grants externos registados, com `lastVerifiedAt`, e se a aprovação de uma reactivação está bloqueada por grants sem reverificação.
8. O pedido de reactivação: quem pediu, e que o aprovador tem de ser outra pessoa.
9. Um aviso permanente de que o modo de adaptadores é `Simulation`, enquanto for. O cabeçalho `X-AgentPortifolio-Simulation` do executor existe para isto.
10. A auditoria por agente, só para o papel `Auditor`.

## O que a interface nunca poderia dizer

- "Revogado", "contido" ou "seguro" sem um passo `Verified` e o seu âmbito.
- Que um token emitido antes da suspensão deixou de funcionar. O código não sabe, e a documentação lida também não o garante.
- Que um estado está bom por falta de dados. Ausência de leitura é "desconhecido", não "ok".
- Que `Suspended` implica contenção externa.

## Regras para quando for escrita

- Só chama a Control API. Não lê a base de dados e não tem credencial do executor.
- Não guarda tokens nem conteúdo de documentos.
- Mostra o erro tal como a API o devolve (código HTTP e o campo `error`, ou `reasonCode` no executor), sem o traduzir para uma mensagem mais simpática que altere o sentido.
- Os papéis aplicam-se na API. A interface esconder um botão não é controlo de acesso.
