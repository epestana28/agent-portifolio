# 05. Verificar a ledger e os digests

Estado deste runbook: escrito a partir da documentação oficial do SQL Ledger, sem subscrição e sem base de dados em Azure. Nunca executado em Azure. Os comandos T-SQL vêm da documentação; a sua execução real fica para a Fase C.

## Objectivo

Provar, com evidência, que a ledger não foi adulterada, e que os digests que a protegem existem, são recentes e estão em armazenamento que quem administra a base de dados não consegue alterar.

O que a ledger dá: evidência de adulteração depois de correr a verificação contra digests guardados fora da base de dados. Não impede a adulteração, não impede uma entrada inicial falsa, não garante disponibilidade e não recupera dados. Sem digests externos fiáveis e sem verificação periódica, a garantia não existe na prática: a verificação e a integridade da base dependem da integridade dos digests de entrada.

## Quem pode executar

Papel `AgentPortifolio.Auditor` na aplicação e, no SQL, um principal Entra com a permissão `VIEW LEDGER CONTENT` (verificação) e, se for preciso gerar um digest manual, `GENERATE LEDGER DIGEST`. Com autenticação só Entra, não existe conta SQL de recurso.

Quem verifica não deve ser quem escreve na ledger nem quem administra a conta de digests. A separação de privilégios entre SQL, destino de digests e verificação é um requisito do desenho.

## Pré-requisitos

1. Upload automático de digests configurado e a funcionar (recurso `ledgerDigestUploads` no Bicep, ligado só depois de o servidor SQL ter o papel `Storage Blob Data Contributor` na conta de digests).
2. Política de imutabilidade no contentor `sqldbledgerdigests`, com `allowProtectedAppendWrites`, e (em produção) bloqueada. Em ambiente de teste pode estar desbloqueada. Ver o README do Bicep.
3. `ALLOW_SNAPSHOT_ISOLATION` activo na base de dados. A documentação exige-o antes de correr as procedures de verificação.
4. Acesso ao SQL por Entra, a partir de um cliente que o suporte.
5. Janela de execução adequada: a verificação pode consumir muitos recursos em bases grandes. Combinar fora de picos.
6. Com autenticação só Entra, Elastic Jobs não é suportado. A verificação periódica agendada usa Azure Automation (indicado na documentação) ou um job próprio da organização, ainda por definir na Fase C.

## Passos

1. Confirmar a configuração dos digests.
   - O contentor deve chamar-se `sqldbledgerdigests`, com caminho do tipo `ServerName/DatabaseName/CreationTime`, na conta de armazenamento esperada.
   - Ver que o destino não foi alterado para um armazenamento sem política de imutabilidade bloqueada. A documentação avisa que um utilizador privilegiado pode mudar a localização dos digests para um armazenamento desprotegido. Rever a configuração e a política da conta no portal ou pelo comando aprovado.
   - A conta de armazenamento não pode ser LRS (limite documentado do upload automático).
2. Ler as localizações de digest conhecidas da base de dados:

   ```sql
   SELECT * FROM sys.database_ledger_digest_locations;
   ```

3. Medir a idade do último digest.
   - Os digests são gerados num intervalo de 30 segundos e carregados para o destino, mas só se houver transacções nesse intervalo. Ausência de digest durante um período sem transacções não é falha.
   - Obter a hora do último commit na ledger:

     ```sql
     SELECT MAX(commit_time) AS last_commit_time
     FROM sys.database_ledger_transactions;
     ```

   - Obter a hora do último digest no armazenamento: o blob mais recente do contentor (o formato JSON do digest inclui `digest_time` e `last_transaction_commit_time`). Ler o contentor pelo método aprovado. O comando exacto de listagem não foi verificado nesta pesquisa: confirmar na documentação actual.
   - Comparar. Idade do último digest = agora menos `digest_time`. Se existiram commits depois do `last_transaction_commit_time` do último digest e o intervalo é maior que o limite aprovado pela organização, é falha de exportação. O limite não está fixado neste runbook.
4. Correr a verificação contra o armazenamento de digests, com o script da documentação:

   ```sql
   DECLARE @digest_locations NVARCHAR(MAX) = (SELECT * FROM sys.database_ledger_digest_locations FOR JSON AUTO, INCLUDE_NULL_VALUES);
   SELECT @digest_locations AS digest_locations;
   BEGIN TRY
       EXEC sys.sp_verify_database_ledger_from_digest_storage @digest_locations;
       SELECT 'Ledger verification succeeded.' AS Result;
   END TRY
   BEGIN CATCH
       THROW;
   END CATCH
   ```

5. Interpretar. O código de retorno de `sp_verify_database_ledger` e de `sp_verify_database_ledger_from_digest_storage` é `0` em sucesso e `1` em falha. A procedure devolve uma linha com a coluna `last_verified_block_id`. Registar esse valor.
6. Para verificar uma tabela só, a procedure aceita o parâmetro opcional `@table_name`. Útil para a tabela de eventos de auditoria.
7. Opcional, para um ponto de controlo imediato: gerar um digest manual (`EXECUTE sp_generate_database_ledger_digest;`, exige `GENERATE LEDGER DIGEST`) e voltar a verificar. Um digest manual fecha o bloco corrente.
8. Registar o resultado e a idade do último digest. Programar a próxima verificação. A frequência é decidida pelo cliente e aprovada pela organização, e este runbook não a fixa.

### Se a verificação falhar, ou a idade do digest for excessiva

Parar e não "corrigir" nada na base de dados. Uma falha de verificação é um incidente de segurança até prova em contrário.

1. Declarar o incidente, abrir ticket e avisar o responsável de segurança.
2. Preservar o estado: não restaurar, não apagar blobs, não alterar permissões antes de recolher evidência. Guardar o resultado da verificação, o JSON das localizações de digest e a lista de blobs do contentor.
3. Distinguir as causas prováveis, sem concluir antes de ter prova:
   - Falha de exportação: permissões da identidade do servidor na conta revogadas, ou configuração de rede que torna a conta inacessível. A documentação refere ambas como causas de upload falhado. Rever também se alguém desactivou o upload automático.
   - Restauro recente: a base ganhou uma nova "incarnation", e a verificação usa os digests de todas as incarnations. Ver o runbook 06.
   - Adulteração: o hash calculado não coincide com o digest. A documentação tem uma página própria sobre recuperação depois de adulteração, que deve ser lida na íntegra antes de agir. Página: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-how-to-recover-after-tampering
4. Tratar a auditoria do intervalo afectado como não fiável. Usar evidência independente: registos de início de sessão e auditoria do Entra, registos do APIM, registos do SharePoint e do Microsoft 365, e o Service Bus.
5. A suspensão dos agentes cujas acções caem no intervalo afectado é decisão de um `PolicyAdmin`, caso a caso (runbook 01). Não é automática.
6. Corrigir a causa (por exemplo repor o papel de escrita do servidor na conta) e voltar a verificar. Registar tudo.

## Como verificar

1. A verificação devolveu o código `0` e a mensagem de sucesso, com `last_verified_block_id` registado.
2. A idade do último digest está dentro do limite aprovado, tendo em conta que não há digest sem transacções.
3. A política de imutabilidade do contentor existe, protege escritas append, e está bloqueada em produção.
4. O destino dos digests é o esperado, e não foi trocado.
5. Existem alertas configurados para falhas de upload e para queda do número de uploads com sucesso (a documentação recomenda ambos). Os nomes exactos das métricas não foram verificados: confirmar na documentação actual.
6. Para o teste BT-16 (alteração de evidência é detectada nos cenários suportados), repetir em ambiente de teste: alterar dados fora da API e confirmar que a verificação falha. Só em ambiente de teste, nunca em produção.

## Evidência a registar

- Data e hora UTC, quem executou, base de dados e servidor.
- Resultado da verificação (sucesso ou falha), `last_verified_block_id` e código de retorno.
- JSON das localizações de digest usado.
- Idade do último digest, `last_commit_time` e `digest_time` comparados.
- Estado da política de imutabilidade (período, bloqueada ou não) e do destino.
- Em falha: tudo o que se preservou, causa provável, decisões e ticket.

Os digests contêm nome da base de dados, identificador de bloco, hash, hora do último commit e hora do digest, e o caminho do blob contém nome do servidor e da base. Não contêm tokens, segredos nem conteúdo. Mesmo assim, não colar o conteúdo dos blobs em tickets.

## Limites conhecidos

- A ledger oferece integridade para a frente (forward integrity): evidência de adulteração em dados que estão na ledger. Não prova que a entrada original era verdadeira.
- Quem controla a máquina ou os ficheiros pode alterar dados. A ledger não o impede e garante que será detectado quando se verificar contra digests fiáveis.
- A verificação é intensiva em recursos em bases grandes. A documentação não fixa o tempo.
- Não há forma suportada de apagar linhas em tabelas ledger append-only. Dados pessoais não devem entrar na ledger nem no armazenamento imutável.
- O Azure Confidential Ledger oferece uma garantia mais forte contra administradores privilegiados, com autorização própria, mas tem limites (por exemplo 2 ledgers por subscrição por omissão) e replica para regiões emparelhadas sem opção de a desactivar. Fica como opção avaliada, e não está no Bicep.
- O tamanho máximo de transacção em bytes não está documentado. A documentação só dá o limite de 200 tabelas ledger por transacção.
- A sintaxe `GRANT` exacta das permissões `VIEW LEDGER CONTENT` e `GENERATE LEDGER DIGEST` não foi lida: confirmar na documentação actual.
- A frase literal de disponibilidade geral da ledger no Azure SQL Database não foi encontrada. Todas as páginas de ledger se aplicam ao Azure SQL Database sem aviso de preview.

## Documentação oficial (verificada a 2026-11-11)

- Gestão de digests: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management
- Configurar digests automáticos: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-how-to-enable-automatic-digest-storage
- Verificação da base de dados: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-verification
- `sp_verify_database_ledger_from_digest_storage`: https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sys-sp-verify-database-ledger-from-digest-storage-transact-sql
- Monitorizar uploads de digests: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-monitor-digest-uploads
- Recuperar depois de adulteração: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-how-to-recover-after-tampering
- Limites da ledger: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits
- Blob imutável, visão geral: https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview
