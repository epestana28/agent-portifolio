# Plano de testes

Este documento liga os 20 testes bloqueantes da secção 15 da especificação (BT-01 a BT-20) aos testes que existem em `tests/`, diz o que cada um prova e diz o que falta para uma prova real.

Regra de leitura: tudo o que corre hoje corre contra `SimulatedMicrosoftEnvironment`. Esse simulador só prova a lógica de controlo deste produto (admissão, estado, epoch, contenção, auditoria). Não prova nada sobre o comportamento do Entra, do Graph, do SharePoint, do APIM ou do Azure SQL. Quando o texto diz "provado localmente", quer dizer "a lógica do produto reage correctamente ao que o simulador lhe dá".

## 1. Resultado medido

Em 2026-11-11, `dotnet test` com `AP_TEST_SQL` definido contra um contentor `mcr.microsoft.com/mssql/server:2022-latest` deu 78 testes, 78 passaram, 0 falharam.

Sem `AP_TEST_SQL`, passam 73 e 5 ficam ignorados. Os 5 são os testes da classe `SqlLedgerTests`, que declaram o motivo do salto: `AP_TEST_SQL is not set; SQL Server ledger tests were not run.` Um salto não é uma passagem. Um relatório que mostre 73 verdes sem dizer que 5 foram ignorados está incompleto.

O número 78 conta cada linha de `[Theory]` como um teste. Há 60 métodos de teste.

## 2. Como correr

Todos os testes, sem SQL Server:

```bash
dotnet test
```

Com SQL Server real, em `AP_TEST_SQL` vai a cadeia de ligação ao servidor, não a uma base de dados:

```bash
export AP_TEST_SQL="Server=localhost,1433;User Id=sa;Password=<a tua password>;Encrypt=True;TrustServerCertificate=True"
dotnet test
```

Um servidor SQL Server 2022 de teste, a correr à parte (o serviço `sql` do `docker-compose.yml` não publica a porta 1433 no anfitrião, por isso não serve para isto sem alterar o ficheiro):

```bash
docker run -d --name ap-sql-test -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="<a tua password>" -p 127.0.0.1:1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

O teste cria a sua própria base de dados (`ap_test_<guid>`), aplica o esquema duas vezes para provar que é idempotente e apaga a base no fim. Não toca em nenhuma outra base. A conta precisa de poder criar bases de dados e de `ENABLE LEDGER`, como `sa`. Para remover o servidor no fim: `docker rm -f ap-sql-test`.

Um só BT, pelo prefixo do método:

```bash
dotnet test --filter "DisplayName~BT16"
```

A convenção é o prefixo `BTnn_` no nome do método. Os testes que provam o mesmo BT por outro ângulo partilham o prefixo.

Os testes de HTTP (`HttpBoundaryTests`) usam `WebApplicationFactory` com `Auth:Mode=Development` e `Adapters:Mode=Simulation`. Não correm os hosts reais num contentor.

## 3. Matriz BT-01 a BT-20

Estado, um de quatro valores:

- `Provado localmente`: a lógica do produto está provada, com `InMemoryWalletStore` e mundo simulado.
- `Provado localmente contra SQL Server 2022 real`: igual, e há pelo menos um teste que corre contra o motor SQL Server 2022 real, só com `AP_TEST_SQL`.
- `Só em simulação`: a afirmação é sobre o comportamento real de um serviço Microsoft e só existe a simulação. Não conta como prova.
- `Por provar em tenant (fase C/D)`: não há prova local suficiente. Precisa de tenant ou de infraestrutura Azure.

| Id | Teste (secção 15) | Estado | Métodos de teste | O que falta para prova real |
|---|---|---|---|---|
| BT-01 | Recurso autorizado funciona, não autorizado falha | Provado localmente | `BT01_Authorised_resource_works_and_unauthorised_resource_fails`, `BT01_Effective_access_is_the_intersection_of_wallet_and_external_grant` | Repetir com os recursos reais e um recurso negativo reais, no tenant (fase C). A matriz de permissões por endpoint continua por validar. |
| BT-02 | Chamador e tenant falsificados falham | Provado localmente | `BT02_Forged_caller_and_forged_tenant_fail`, `BT02_Identity_comes_from_the_token_never_from_the_body` | Os tokens são de desenvolvimento. Falta provar com tokens reais do Entra e confirmar que `tid` e `oid` têm o significado assumido (P-02, P-19). |
| BT-03 | Suspensão invalida capacidade anterior ainda dentro da validade | Provado localmente | `BT03_Suspension_invalidates_a_capability_that_is_still_inside_its_validity`. Complementares: `Capability_from_an_earlier_epoch_is_dead_even_inside_its_validity`, `Agent_reads_through_http_and_is_refused_after_suspension` | Repetir com um token capturado antes da suspensão e reutilizado através do APIM real (TC-03, fase C). |
| BT-04 | Corrida suspensão e execução documenta exactamente as acções em curso | Provado localmente | `BT04_Race_between_suspension_and_execution_reports_exactly_the_actions_in_flight`, `BT04_Suspension_between_admission_and_revalidation_cancels_before_any_external_effect` | A corrida é orquestrada com uma barreira no simulador, não com tráfego real. A corrida sob carga em SQL real não está provada para a suspensão (só para o limite de acções, ver `Concurrent_admissions_respect_the_limit_and_idempotency_under_real_locking`). |
| BT-05 | Gateway e backend não podem ser contornados | Por provar em tenant (fase C/D) | Só aproximação local: `BT05_Backend_cannot_be_used_without_a_token_or_with_a_token_for_the_other_audience`, `Generic_proxy_batch_and_arbitrary_urls_are_impossible` | O teste local prova só que o executor recusa pedidos sem token e com a audiência errada, e que não há proxy genérico nem batch. NÃO prova que o gateway não é contornável: não há APIM, nem rede, nem identidade do executor. Fase C (TC-17). |
| BT-06 | O agente não tem credencial alternativa para Graph | Só em simulação | `BT06_Executor_never_returns_a_credential_to_the_agent` | O teste procura marcadores (`access_token`, `Bearer`, `Authorization`, `downloadUrl`, `secret`, o `ExecutorObjectId`) nas respostas e cabeçalhos do executor simulado. Não prova que o runtime do agente não tem rota nem credencial para Graph. Fase C e D (TC-20). |
| BT-07 | Retirar uma capacidade não altera outras indevidamente | Provado localmente | `BT07_Revoking_one_capability_does_not_change_the_others` | Nada de específico do tenant. |
| BT-08 | SQL indisponível: negar novas acções protegidas | Provado localmente contra SQL Server 2022 real | `BT08_Store_unavailable_denies_new_protected_actions`, `BT08_Result_that_cannot_be_recorded_is_not_handed_over_as_success`. Contra SQL real: `Unreachable_database_fails_closed` | Os dois `BT08_` correm em memória, com `Available = false`. O de SQL real usa um servidor inalcançável (`127.0.0.1,1`). Falta falhar um Azure SQL real (failover, firewall, throttling), na fase C. |
| BT-09 | Bus indisponível: o bloqueio persistente continua eficaz | Provado localmente | `BT09_Bus_unavailable_the_persisted_block_stays_effective` | Não existe Service Bus. O teste prova que, sem worker e sem entrega, o bloqueio local persiste e o relatório diz só `LocalBlocked` com âmbito `local admission only`. Quando houver bus, é preciso repetir com o bus indisponível. |
| BT-10 | Repetição de mensagem não duplica efeito indevidamente | Provado localmente contra SQL Server 2022 real | `BT10_Repeated_request_with_the_same_key_does_not_repeat_the_effect`, `BT10_Redelivered_containment_message_does_not_repeat_verified_steps`. Contra SQL real: `Concurrent_admissions_respect_the_limit_and_idempotency_under_real_locking` (10 pedidos concorrentes com a mesma chave, um só efeito) | Entrega repetida por Service Bus real não foi exercida. Idempotência dos efeitos no Entra e no Graph não é garantida por este produto. |
| BT-11 | Timeout externo produz unknown e reconciliação, não sucesso inventado | Provado localmente | `BT11_External_timeout_produces_unknown_then_reconciliation_never_invented_success`, `BT11_Timeout_of_the_data_call_is_recorded_as_unknown` | O timeout é injectado no simulador. O que os serviços reais fazem ao pedido que expirou (aplicaram-no ou não) só se vê no tenant (fase D). |
| BT-12 | Throttling Microsoft gera estado parcial visível | Provado localmente | `BT12_Throttling_beyond_the_budget_leaves_a_visible_partial_state`, `BT12_Throttling_inside_the_budget_honours_retry_after_and_then_completes` | O `Retry-After` é o que o simulador devolve. Os limites e cabeçalhos reais do Graph são NÃO VERIFICADOS por estes testes (fase D). |
| BT-13 | Desactivar um agente não desactiva irmãos do blueprint | Só em simulação | `BT13_Disabling_one_agent_does_not_disable_siblings_of_the_same_blueprint` | Prova só que o adaptador simulado actua num objecto e que o produto não suspende o irmão. O efeito real de desactivar uma agent identity sobre irmãos e sobre o agent user associado é NÃO VERIFICADO e só se vê no tenant (fase D). |
| BT-14 | Testar token previamente emitido num recurso de teste e reportar o resultado real | Só em simulação | `BT14_SIMULATED_Token_issued_before_suspension_keeps_the_report_from_claiming_verified` | Prova só que o relatório não afirma `VerifiedScoped` enquanto a sonda aceita um token anterior, e que a limitação fica no relatório. O resultado real depende da avaliação contínua de acesso e da emissão no tenant. Fase D, por recurso e por tipo de identidade. |
| BT-15 | Drift de grants é detectado e tratado | Provado localmente | `BT15_Broad_external_grant_is_detected_and_suspends_the_agent`, `BT15_Direct_grant_on_the_agent_identity_lowers_coverage_and_clean_state_restores_it` | Os grants vêm do simulador. Falta a leitura real de grants (SharePoint Selected e permissões consentidas no Entra), fase C e D. A lista de nomes de âmbito largo não é exaustiva. |
| BT-16 | Alteração de evidência é detectada nos cenários suportados pela ledger | Provado localmente contra SQL Server 2022 real | `BT16_Ledger_table_rejects_update_and_delete`, `BT16_Ledger_verifies_against_its_digest_and_a_wrong_digest_is_detected` | Prova que o motor rejeita `UPDATE`, `DELETE` e `TRUNCATE` e que `sys.sp_verify_database_ledger` aceita o digest gerado e rejeita um digest adulterado. Os digests são gerados na própria base, não estão em armazenamento imutável independente. Falta: digests externos, verificação agendada, alteração real dos ficheiros de dados, Azure SQL. Fase C (TC-19). |
| BT-17 | Restauro e failover não reactivam um agente suspenso, com RPO e reconciliação validados | Por provar em tenant (fase C/D) | Só aproximação: `Vertical_slice_runs_on_sql_server_and_BT17_a_restart_does_not_reactivate` | O teste cria novos objectos de armazém e serviços sobre a mesma base ("reinício"). NÃO é restauro nem failover, e não valida RPO. Falta: restauro de uma cópia anterior à suspensão, failover geo, reconciliação antes de reabrir tráfego. Fase C. |
| BT-18 | Sem segredos ou conteúdo sensível desnecessário na ledger e nos logs | Provado localmente | `BT18_Ledger_holds_no_document_content_no_item_identifier_and_no_token`. Complementar: `Every_decision_leaves_the_minimum_audit_fields` | O teste serializa a auditoria, as acções e as operações de contenção em memória e procura conteúdo, `itemId`, token e `Bearer`. Não cobre os registos (logs) dos hosts. Não corre contra a ledger SQL. Falta um teste sobre os registos reais e sobre a base. |
| BT-19 | Operador sem papel não consegue suspender, reactivar ou conceder capacidades | Provado localmente | `BT19_Operator_without_the_role_cannot_suspend_reactivate_or_grant`, `BT19_Roles_are_enforced_on_the_control_api` | Os papéis chegam em tokens de desenvolvimento. Falta provar que o Entra emite os papéis `AgentPortifolio.*` nos tokens reais (P-19). |
| BT-20 | Break-glass funciona com a aplicação indisponível | Por provar em tenant (fase C/D) | Nenhum | Não existe teste. O procedimento está em `runbooks/04-break-glass.md`. Precisa de um ensaio com a aplicação parada, no tenant, fase D. |

Contagem: 11 provados localmente (BT-01, BT-02, BT-03, BT-04, BT-07, BT-09, BT-11, BT-12, BT-15, BT-18, BT-19), 3 provados localmente contra SQL Server 2022 real (BT-08, BT-10, BT-16), 3 só em simulação (BT-06, BT-13, BT-14) e 3 por provar em tenant (BT-05, BT-17, BT-20). Total: 20.

Nenhum BT está provado em tenant. Nenhum dos testes acima exerce um serviço Microsoft real.

## 4. O que a simulação não prova

Para não ler mais do que o que está medido:

- `SimulatedMicrosoftEnvironment` é código nosso. Responde o que lhe mandámos responder. Que o produto reaja bem ao simulador não diz que o Entra, o Graph ou o SharePoint se comportem assim.
- Os quatro adaptadores de contenção (`identity.disable`, `grants.remove`, `runtime.isolate`, `tokens.probe`) são simulados. A verificação de cada passo é uma sonda simulada.
- O tempo é um relógio falso. Expiração de tokens e de capacidades prova a lógica de comparação, não a emissão real.
- O `InMemoryWalletStore` serializa transacções com um semáforo. Não exerce concorrência de motor. A concorrência real só está exercida em `Concurrent_admissions_respect_the_limit_and_idempotency_under_real_locking`.
- A autenticação em modo `Development` usa uma chave simétrica local. O modo `Entra` nunca foi exercido.
- Nada foi testado em Azure SQL. O motor usado foi SQL Server 2022 num contentor. Diferenças de comportamento (por exemplo na ledger e nos digests) são NÃO VERIFICADAS.

## 5. Testes extra, sem prefixo BT

Os que provam regras do domínio e do ciclo de vida, que a especificação não numera. Por ficheiro.

`tests/Unit/DomainTests.cs`:

- `Non_canonical_resources_are_rejected_not_normalised` (várias linhas de `[Theory]`)
- `Canonical_resource_round_trips`
- `Operations_outside_the_allowlist_are_rejected` (`[Theory]`)
- `Unknown_missing_or_extra_arguments_are_rejected`
- `Item_identifiers_cannot_carry_paths_or_queries` (`[Theory]`)
- `Arguments_digest_is_stable_and_does_not_contain_the_arguments`
- `Valid_capability_is_allowed`
- `Capability_from_an_earlier_epoch_is_dead_even_inside_its_validity`
- `Expired_revoked_and_missing_capabilities_are_denied_with_distinct_reasons`
- `Capability_of_another_agent_or_tenant_never_matches`
- `Only_active_agents_are_admitted` (`[Theory]`)
- `Limits_and_wallet_expiry_are_enforced`
- `Verified_requires_every_step_verified`

`tests/Integration/LifecycleTests.cs`:

- `Activation_needs_a_second_person`
- `Agent_without_owner_or_with_shared_executor_identity_cannot_be_registered` (recusa executor igual à identidade do agente e dono vazio. NÃO cobre dois agentes diferentes com o mesmo `ExecutorObjectId`: esse caso, TC-10, não está implementado)
- `Reactivation_needs_second_person_revalidated_grants_and_kills_old_capabilities`
- `Old_containment_message_never_acts_on_a_reactivated_agent_and_never_reactivates`
- `Suspend_is_idempotent_and_does_not_bump_the_epoch_twice`
- `Capability_must_be_allowlisted_canonical_and_short_lived`
- `Retired_agent_stays_denied`

`tests/Integration/VerticalSliceTests.cs`:

- `Every_decision_leaves_the_minimum_audit_fields`
- `Tenants_are_isolated_for_operators_and_for_callers`
- `Rate_limit_of_the_wallet_is_enforced`

`tests/Security/HttpBoundaryTests.cs`:

- `Agent_reads_through_http_and_is_refused_after_suspension`
- `Operator_of_another_tenant_cannot_see_or_touch_the_agent`
- `Generic_proxy_batch_and_arbitrary_urls_are_impossible` (`[Theory]`)

`tests/FailureInjection/FailureTests.cs`:

- `Repeated_failure_is_bounded_and_reported_as_failed`
- `Suspended_never_becomes_verified_without_external_steps`
- `Message_that_keeps_failing_is_dead_lettered_and_the_operation_marked_failed`

`tests/Integration/SqlLedgerTests.cs` (todos precisam de `AP_TEST_SQL`):

- `Vertical_slice_runs_on_sql_server_and_BT17_a_restart_does_not_reactivate`
- `Concurrent_admissions_respect_the_limit_and_idempotency_under_real_locking`
- `Unreachable_database_fails_closed`

## 6. Testes de ameaça (TC) a criar

Os `TC-nn` do `threat-model.md` são testes que a especificação não numera. Os que se podem cobrir localmente e ainda não existem como teste dedicado:

- TC-04 (agente A pede o recurso do agente B): coberto em parte por `Capability_of_another_agent_or_tenant_never_matches`, sem teste HTTP dedicado.
- TC-08 (agente de outro tenant devolve resposta igual à de agente inexistente): coberto em parte por `Operator_of_another_tenant_cannot_see_or_touch_the_agent` e `Tenants_are_isolated_for_operators_and_for_callers`. A igualdade das respostas deve ser confirmada.
- TC-09 (o mesmo PolicyAdmin não pede e aprova): coberto por `Reactivation_needs_second_person_revalidated_grants_and_kills_old_capabilities` e `Activation_needs_a_second_person`.
- TC-10 (dois agentes com o mesmo `ExecutorObjectId`): NÃO implementado. Há um teste só para executor igual à identidade do agente.
- TC-14 (mensagem de epoch anterior depois da reactivação): coberto por `Old_containment_message_never_acts_on_a_reactivated_agent_and_never_reactivates`.
- TC-18 (falhas injectadas nunca produzem `VerifiedScoped`): coberto por `Repeated_failure_is_bounded_and_reported_as_failed`, `Suspended_never_becomes_verified_without_external_steps` e os testes BT-11, BT-12 e BT-14.

Os TC de fase C e D (TC-02, TC-03, TC-11, TC-17, TC-19, TC-20) não podem ser escritos sem tenant ou sem infraestrutura Azure.

## 7. Próximos passos de prova

Por ordem de risco, o que fecha mais lacunas:

1. Fase C: Azure de teste com APIM, Azure SQL e armazenamento imutável para digests. Fecha BT-05, BT-17, BT-16 completo e BT-08 contra Azure SQL.
2. Fase C e D: tenant com adaptadores reais. Fecha BT-01 e BT-02 com tokens reais, e BT-06, BT-11, BT-12, BT-13, BT-14 e BT-15 com comportamento real.
3. Fase D: ensaio de break-glass com a aplicação parada. Fecha BT-20.
4. Um teste sobre os registos dos hosts para completar BT-18.
5. Um teste para TC-10, quando a regra existir.

Os adaptadores reais estão bloqueados até os gates da fase A serem aprovados. Enquanto não existirem, esta matriz não pode subir de "Só em simulação" ou "Por provar em tenant".
