# ADR 0007: Falha fechada, sem cache positiva

Estado: proposto. Data de verificação das fontes: 2026-11-11.

## Contexto

Cada admissão decide se um agente pode agir. Se o estado não puder ser lido, há duas escolhas: negar ou presumir que o último estado conhecido ainda vale. Presumir é perigoso. Uma suspensão pode ter acontecido há segundos e uma cache positiva continuaria a admitir.

Os sinais do lado Microsoft não servem como autoridade. A validade de um token de acesso é longa e documentada de forma contraditória. O CAE pode alargar a validade e não cobre tudo. Nada garante que um token já emitido seja recusado logo após a desactivação da identidade.

Estado real: o `AuthorizationEngine` é determinístico, sem I/O e sem cache. Se o repositório falha, o `ExecutionService` recebe `StoreUnavailableException` e devolve `state_unavailable`. Isto foi exercido em memória (`BT08_`) e contra um SQL Server 2022 num contentor, com um servidor inalcançável (`Unreachable_database_fails_closed`). Nunca contra Azure SQL.

## Decisão

1. Cada admissão lê o estado autoritativo em SQL dentro da transacção 1. A execução volta a validar estado, epoch e capacidade na transacção 2, antes de marcar `Executing`.
2. Se o estado não puder ser lido, negar com `state_unavailable`. Nunca se usa o último estado conhecido.
3. Não existe cache positiva de autorização, de estado de agente nem de capacidade, em qualquer camada do produto.
4. O sinal de um token (validade, claims de CAE, ausência de erro do Entra) não substitui o estado em SQL. É só mais uma condição necessária, nunca suficiente.
5. Se o APIM não alcançar o executor, ou o executor não alcançar o Entra para validar o token, o pedido é negado. Não há modo degradado que admita.
6. A indisponibilidade de SQL, de APIM e do Entra é tratada em três ameaças separadas (T-16, T-17 e T-18).
7. Para uma acção externa já iniciada quando o estado falha, o resultado fica `Unknown`. Não se presume sucesso nem falha (ADR 0008).
8. Uma acção que fique em `Executing` para além do prazo é marcada por `SweepStaleActionsAsync` com `stale_execution`. O método existe e tem teste, mas nenhum host o chama hoje: não há agendamento.

## Consequências

- A disponibilidade do SQL é a disponibilidade do produto. Uma falha do SQL nega tudo, incluindo acções legítimas.
- A carga de leitura em SQL cresce com cada pedido. Dimensionar e testar sob carga em tenant (fase C).
- É preciso sinalização para o operador: alertas de `state_unavailable` repetido e de latência de admissão.
- A segurança é preferida à disponibilidade. É uma decisão consciente que o dono do produto tem de aceitar (risco R-12).
- Uma acção cujo resultado não foi gravado devolve `result_not_recorded`. O cliente tem de tratar uma resposta de resultado desconhecido como real.
- As tentativas de agentes suspensos geram `alert.post_suspension_attempt` e servem de sinal de comprometimento.

## Alternativas rejeitadas

- Cache positiva com TTL curto: continua a admitir durante o TTL depois de uma suspensão. Não há valor de TTL seguro quando se exige efeito imediato.
- Falha aberta com registo posterior: viola o princípio de menor privilégio e a regra de falha fechada da especificação.
- Confiar no CAE ou na expiração do token para corte: o CAE tem uma janela até 28 horas para utilizadores e 24 horas para identidades de carga. Não cobre managed identities, e para agentes não está verificado.
- Réplica de leitura com atraso para aliviar a carga: lê estado velho e quebra a revogação imediata.
- Modo degradado só com listas locais: as listas locais são cache positiva por outro nome.

## Evidência documental

Todas verificadas em 2026-11-11.

- Validade por omissão do token de acesso: 60 a 90 minutos numa página e 1 hora noutra. Usa-se 90 minutos como pior caso. https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes
- CAE: tokens até 28 horas para utilizadores e 24 horas para identidades de carga. Só Graph e principais single-tenant de aplicações LOB com `cp1`. Não cobre managed identities. CAE para agentes: NÃO VERIFICADO. https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation-workload
- "Ensure applications revoke their own session tokens and stop accepting Microsoft Entra access tokens even if they're still valid". https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access
- Managed identity: cache de tokens de cerca de 24 horas, sem refresh forçado. https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq
- O efeito da desactivação sobre tokens já emitidos de agent identities: NÃO VERIFICADO. https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin
- A regra de falha fechada sem cache positiva vem da especificação do projecto e não de página Microsoft.

## Estado

Proposto. A aceitação do custo de disponibilidade é decisão humana (R-12).
