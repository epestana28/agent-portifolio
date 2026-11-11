# 07. Repetir os testes de contenção

Estado deste runbook: os testes locais correspondem à Fase B (fatia vertical em .NET 8, sem credenciais reais). O mundo Microsoft é simulado. As fases C, D e E não foram executadas.

## Objectivo

Permitir que uma pessoa independente repita os testes bloqueantes a partir deste documento, saiba o que cada um prova, e saiba quais não podem ser provados localmente.

Um resultado verde local não prova contenção real. Uma API com resposta 200, um dashboard verde ou uma demonstração com adaptadores simulados não provam contenção.

## Quem pode executar

Qualquer engenheiro com acesso ao repositório e a um ambiente com .NET 8 SDK e Docker. Para a repetição de ser independente, de preferência alguém que não escreveu os testes.

Nas fases C e D, os testes reais exigem os papéis Microsoft e a aprovação descritos nos runbooks 01 a 04.

## Pré-requisitos

1. Repositório clonado, no commit que se quer validar. Anotar o hash do commit.
2. .NET SDK 8 (o `global.json` fixa a versão `8.0.100`, com `rollForward` para `latestFeature`).
3. Docker, para um SQL Server 2022 de teste (os 5 testes de `SqlLedgerTests` só correm com ele). Sem ele, esses 5 ficam ignorados e o resultado é 73 testes verdes mais 5 ignorados. Docker Compose só é preciso para o passo 2.
4. Nenhuma credencial real. Os testes locais não precisam delas e não as devem usar. Se alguma for pedida, parar.
5. Não correr contra um tenant ou subscrição reais sem ler os runbooks 01 a 04 e sem aprovação.

## Passos

1. Compilar e correr todos os testes sem SQL Server:

   ```bash
   dotnet test
   ```

   A solução tem testes em `tests/Unit`, `tests/Integration`, `tests/Security` e `tests/FailureInjection`. Só a classe `SqlLedgerTests` (em `tests/Integration`) precisa de SQL Server e salta com a mensagem `AP_TEST_SQL is not set` quando a variável não existe. Os outros correm em memória.
2. Subir um SQL Server 2022 de teste e apontar a variável `AP_TEST_SQL` para o servidor (cadeia de ligação ao servidor, sem base de dados):

   ```bash
   docker run -d --name ap-sql-test -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="<password local>" -p 127.0.0.1:1433:1433 mcr.microsoft.com/mssql/server:2022-latest
   export AP_TEST_SQL="Server=localhost,1433;User Id=sa;Password=<password local>;Encrypt=True;TrustServerCertificate=True"
   ```

   Cada corrida cria a sua própria base (`ap_test_<guid>`) e apaga-a no fim. O serviço `sql` do `docker-compose.yml` na raiz do repositório não publica a porta no anfitrião, por isso não serve para esta variável sem alterar o ficheiro. O `docker compose up` serve para a pilha local completa (SQL Server, `control`, `executor` e `worker`), que precisa de `AP_SQL_PASSWORD`, `AP_DEV_SIGNING_KEY` e `AP_TENANT_ID` no `.env` (o `scripts/quickstart.sh` cria um com valores aleatórios) e percorre a fatia local por HTTP. Não é necessária para `dotnet test`.
3. Correr de novo `dotnet test`, agora com `AP_TEST_SQL` definida. O resultado medido em 2026-11-11 foi 78 testes, 78 passados, 0 falhados.
4. Opcional, para um teste específico: filtrar pelo prefixo do método. Cada teste que cobre um BT chama-se `BTnn_...`, por exemplo:

   ```bash
   dotnet test --filter "DisplayName~BT16"
   ```

   Um teste sem o prefixo `BTnn_` não conta para a lista abaixo. A matriz completa, com os nomes dos métodos, está em `docs/test-plan.md`.
5. Repetir a corrida completa uma segunda vez, para detectar testes instáveis. Um teste que falha numa corrida e passa noutra é um defeito e não um sucesso.
6. Parar o ambiente (`docker rm -f ap-sql-test`, e `docker compose down -v` se tiveres subido a pilha) e remover os volumes de teste. Não deixar dados de teste em ambientes partilhados.
7. Preencher a tabela de resultados abaixo.

## Os 20 testes bloqueantes

Ordem e identificadores da especificação (secção 15). A coluna "Onde se prova" é o estado actual. "Local" quer dizer provado contra o mundo Microsoft simulado. "Local, SQL real" quer dizer que, além disso, há um teste contra um SQL Server 2022 real (só com `AP_TEST_SQL`). "Só simulação" quer dizer que a afirmação é sobre o comportamento real de um serviço Microsoft e só existe a simulação, que não conta como prova. "C/D" quer dizer que só fica provado nas fases C ou D, com recursos reais. Para estes, o que existe localmente é, no máximo, uma aproximação simulada, que não conta como prova.

| ID | O que prova | Onde se prova | Notas |
| --- | --- | --- | --- |
| BT-01 | Recurso autorizado funciona e recurso não autorizado falha. | Local, mundo simulado | A prova com recursos Selected reais é da Fase C. |
| BT-02 | Chamador e tenant falsificados falham (identidade do token, nunca do corpo). | Local | A validação de tokens Entra reais é verificada na Fase C. |
| BT-03 | A suspensão invalida uma capacidade anterior ainda dentro da validade (epoch). | Local | |
| BT-04 | Na corrida suspensão e execução, as acções em curso ficam documentadas exactamente. | Local | A janela real contra o Graph não existe localmente. |
| BT-05 | Gateway e backend não podem ser contornados. | C/D | Exige o APIM e o executor reais com rede e identidades. Local só há aproximação. |
| BT-06 | O agente não tem credencial alternativa para o Graph. | Só simulação | O teste só procura credenciais nas respostas do executor simulado. Exige inventariar acessos reais do agente no tenant. |
| BT-07 | Retirar uma capacidade não altera outras indevidamente. | Local | |
| BT-08 | Com SQL indisponível, as novas acções protegidas são negadas (`state_unavailable`). | Local, SQL real | Há também um teste contra um servidor inalcançável. A indisponibilidade real do Azure SQL é da Fase C. |
| BT-09 | Com o bus indisponível, o bloqueio persistente continua eficaz (a outbox está na base). | Local | Não existe Service Bus: o teste prova o bloqueio sem worker. |
| BT-10 | A repetição de uma mensagem não duplica o efeito. | Local, SQL real | Pedidos concorrentes com a mesma chave contra SQL real. Idempotência dos adaptadores contra serviços reais é da Fase D. |
| BT-11 | Um tempo esgotado externo dá `Unknown` e reconciliação, não sucesso inventado. | Local, com falha injectada | |
| BT-12 | O throttling Microsoft gera estado parcial visível. | Local, com falha injectada | O comportamento real do Graph e do SharePoint é da Fase D. |
| BT-13 | Desactivar um agente não desactiva os irmãos do blueprint. | Só simulação | O efeito real em tenant é da Fase D. |
| BT-14 | Testar um token emitido antes da desactivação num recurso de teste e reportar o resultado real. | Só simulação | O teste local chama-se `BT14_SIMULATED_...` e não conta como prova. Só tem sentido com Entra e recurso reais (C/D). |
| BT-15 | O drift de grants é detectado e tratado. | Local, mundo simulado | A reconciliação com grants reais é da Fase C. |
| BT-16 | A alteração de evidência é detectada nos cenários suportados pela ledger. | Local, SQL real | Os testes `BT16_` provam que o motor rejeita `UPDATE`, `DELETE` e `TRUNCATE` e que um digest adulterado falha a verificação. Digests em armazenamento imutável independente e Azure SQL só na Fase C. |
| BT-17 | Restauro e failover não reactivam um agente suspenso; RPO validado e reconciliação antes de reabrir tráfego. | C/D | Local só há um reinício dos serviços sobre a mesma base, que não é restauro nem failover. Exige restauro ou failover reais. Ver o runbook 06. |
| BT-18 | Não há segredos nem conteúdo sensível desnecessário na ledger nem nos logs. | Local | O teste cobre a auditoria, as acções e as contenções em memória. Não cobre os registos dos hosts nem a ledger SQL. Rever também os logs reais na Fase C. |
| BT-19 | Um operador sem papel não consegue suspender, reactivar nem conceder capacidades. | Local | A atribuição de papéis reais no Entra é da Fase C. |
| BT-20 | O break-glass funciona com a aplicação indisponível. | C/D | Não há teste automático. Só se prova ensaiando o runbook 04 com a aplicação desligada. |

Resumo: BT-05, BT-17 e BT-20 só ficam provados nas fases C ou D, e BT-06, BT-13 e BT-14 só existem em simulação. Os restantes ficam provados no âmbito da simulação local (BT-08, BT-10 e BT-16 também contra SQL Server 2022 real), e a parte que toca em serviços Microsoft reais é confirmada nas fases C e D.

## Como verificar

1. Todos os testes passam nas duas corridas. Na corrida com `AP_TEST_SQL` não há testes ignorados. Na corrida sem ela, os 5 de `SqlLedgerTests` ficam ignorados por desenho e têm de ser declarados como tal. Qualquer outro teste ignorado sem justificação conta como falha.
2. Cada BT-01 a BT-20 tem pelo menos um teste identificado, ou fica marcado como "só C/D" com a explicação. Um BT sem teste e sem marca é lacuna.
3. Os seis testes que só se provam em C/D ou só existem em simulação (BT-05, BT-06, BT-13, BT-14, BT-17 e BT-20) não aparecem como "provados". Se algum relatório os apresenta como provados com base em execução local, o relatório está errado.
4. A injecção de falhas produz `Unknown`, `Partial` e `Failed` quando devia, e nunca `VerifiedScoped` por engano. `Suspended` nunca passa sozinho a `VerifiedScoped`.
5. Nenhuma credencial real foi usada.

## Evidência a registar

- Hash do commit, data e hora, quem executou, sistema operativo e versões do .NET SDK e do Docker.
- Resultado completo da corrida (número de testes, passados, falhados, ignorados), das duas corridas.
- Tabela dos 20 testes preenchida com: teste ou testes que o cobrem, resultado, e onde se prova (local ou C/D).
- Testes instáveis encontrados e o que se fez.
- Desvios a este runbook.

Os relatórios de teste não devem conter segredos. Rever antes de os partilhar.

## Limites conhecidos

- O mundo Microsoft local é uma simulação (adaptadores de Entra, remoção de grants, isolamento de runtime e sonda de token antigo). Não prova o comportamento real de Entra, Graph, SharePoint ou Exchange.
- Não existe adaptador real para Entra nem para Graph na altura em que este runbook foi escrito.
- A injecção de falhas cobre as falhas que alguém previu. Não cobre as que a Microsoft pode introduzir.
- BT-05, BT-06, BT-13, BT-14, BT-17 e BT-20 não se provam localmente.
- A definição de concluído do MVP exige um agente real num ambiente de teste, e uma pessoa independente a repetir os testes a partir do runbook. Este documento é só a parte local desse requisito.
- A convenção de nomes é o prefixo `BTnn_` no método. Um BT pode ter vários testes. Os nomes exactos estão em `docs/test-plan.md`.
