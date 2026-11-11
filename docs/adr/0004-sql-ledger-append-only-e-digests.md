# ADR 0004: SQL Ledger append-only e digests

Estado: proposto. Data de verificação das fontes: 2026-11-11.

## Contexto

É preciso um histórico verificável das decisões e execuções: quem pediu, o que foi decidido, em que versão e epoch, com que resultado. A especificação manda preferir tabelas ledger append-only no Azure SQL Database, com digests em armazenamento independente protegido, e proíbe implementar cadeia de hashes própria como substituto.

Estado real: `ap.AuditEvent` é uma tabela ledger append-only em `src/Storage.SqlServer`, exercida localmente em SQL Server 2022 num contentor (`BT16_`, com `AP_TEST_SQL`), com digests gerados na própria base. Não existe base de dados Azure SQL nem armazenamento de digests: o Bicep está escrito e nunca foi aplicado. Por omissão o código tenta criar o esquema no arranque (`Storage:EnsureSchema`), o que exige `ENABLE LEDGER`: em ambientes com o ponto 4 desta decisão, pôr `false` e aplicar o esquema com conta administrativa.

## Decisão

1. Eventos de domínio em tabela ledger append-only (`WITH (LEDGER = ON (APPEND_ONLY = ON))`). As tabelas de estado, que precisam de `UPDATE`, são tabelas normais. Isto pressupõe que a base de dados não é ledger (`isLedgerOn` desligado): pressuposto P-11, decisão humana na fase A.
2. Digests automáticos para Azure Blob com política de imutabilidade validada e `locked` depois de aprovada a retenção. Redundância que não seja LRS. Azure Confidential Ledger como destino alternativo, se a separação de confiança o justificar.
3. Verificação periódica da ledger contra os digests, e após restauro ou failover. O agendamento usa Azure Automation ou um job próprio, porque Elastic Jobs não é suportado com autenticação Entra-only.
4. Privilégios separados: uma identidade que só insere eventos, uma que só verifica (`VIEW LEDGER CONTENT`), e nenhuma com `ENABLE LEDGER` em tempo de execução. A separação concreta é inferência e fica por validar.
5. A identidade do agente, o iniciador humano e a identidade executora vão em colunas do evento, porque o `principal_name` do ledger é o login original da ligação (a identidade da aplicação).
6. A ledger guarda um digest SHA-256 dos argumentos e referências minimizadas, nunca tokens, segredos, conteúdo, prompts nem dados pessoais desnecessários.
7. Monitorizar idade do último digest, falhas de exportação, resultado da verificação e cobertura temporal.

### Porque não uma cadeia de hashes própria

- O SQL Ledger já faz hash por transacção, agrupa-as em blocos com raiz de uma árvore de Merkle e inclui o hash do bloco anterior. Uma segunda cadeia dentro da mesma base repete isso.
- Traz digests, formato de digest documentado, upload automático para armazenamento imutável e procedimentos de verificação (`sp_verify_database_ledger_from_digest_storage`). Uma cadeia própria exigiria reescrever tudo isto.
- Uma cadeia própria guardada na mesma base tem a mesma limitação de qualquer ledger: quem controla a máquina refaz a cadeia. Só ganharia valor com uma âncora externa, que é exactamente o digest do SQL Ledger (inferência).
- Código criptográfico próprio é superfície de erro sem ganho de garantia.
- Um auditor pode usar os procedimentos documentados da Microsoft em vez de rever uma implementação do projecto.
- A especificação proíbe-o expressamente.

## Consequências

- A ledger dá evidência de adulteração depois de se verificar contra digests de confiança. Não é prevenção, não impede uma entrada inicial falsa, não garante disponibilidade e não recupera dados adulterados.
- Sem digests externos de confiança e verificação periódica, a garantia não existe na prática.
- Não se apaga nada: sem `TRUNCATE`, sem apagar dados antigos, tabelas e colunas eliminadas mantêm-se fisicamente. Uma política `locked` só aumenta. Definir pseudonimização, retenção e eliminação antes de bloquear a imutabilidade (risco R-05). Não se declara conformidade RGPD por usar serviços Microsoft.
- Ledger de base de dados e de tabela são decisões irreversíveis. Não se converte uma tabela normal em ledger nem o contrário.
- Limites a respeitar: 200 tabelas ledger por transacção, só colunas nulas novas, tipos de dados não suportados (`XML`, `SqlVariant`, tipos definidos pelo utilizador, `FILESTREAM`, `Vector`), sem replicação transaccional.
- Como a ledger não deixa corrigir uma linha duplicada, quem escreve eventos tem de ser idempotente (ADR 0005).
- O verificador consome recursos da base e exige `ALLOW_SNAPSHOT_ISOLATION`.
- A maior parte da configuração real não foi validada: lock da política em Bicep, pré-criação do contentor `sqldbledgerdigests`, identidade user-assigned para upload, comportamento com rede privada.

## Alternativas rejeitadas

- Cadeia de hashes própria: ver acima.
- Base de dados ledger inteira (`isLedgerOn`): todas as tabelas passam a ledger, as operacionais ficam actualizáveis com tabela de histórico que não se apaga, e a escolha é irreversível.
- Tabela normal com permissões, triggers e backups: um administrador altera os dados sem rasto verificável.
- Azure Confidential Ledger como armazenamento principal dos eventos: a documentação recomenda-o como armazenamento de digests de dados relacionais. Tem entradas de 8 KiB, dois ledgers por subscrição por omissão, apagar é irreversível, replica sempre para o par regional, e o modelo não é relacional.
- Application Insights ou Monitor como evidência: não substituem a ledger.
- Blob imutável sem ledger: perde a verificação de integridade por transacção.

## Evidência documental

Todas verificadas em 2026-11-11.

- Tamper-evidence, não prevenção: "an attacker or system administrator who has control of the machine can bypass all system checks ... Ledger can't prevent such attacks but guarantees that any tampering will be detected when the ledger data is verified". https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-overview
- Blocos fecham a cada cerca de 30 segundos ou aos 100K transacções. `sys.database_ledger_blocks` guarda a raiz da árvore de Merkle e o hash do bloco anterior "to form a blockchain". `principal_name` é `ORIGINAL_LOGIN()`. https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-ledger
- Sintaxe `WITH (LEDGER = ON (APPEND_ONLY = ON))` e bloqueio de `UPDATE` e `DELETE`. https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-how-to-append-only-ledger-tables e https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-append-only-ledger-tables
- Limites (sem converter, sem apagar, 200 tabelas por transacção, LRS não suportado para digests). https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits
- Digests automáticos a cada 30 segundos, só havendo transacções. Contentor `sqldbledgerdigests`. "Azure Confidential Ledger has a stronger integrity guarantee for customers who might be concerned about privileged administrators access to the digest". Comportamento em restauro, failover e geo-replicação. https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management
- Verificação, `last_verified_block_id`, códigos `0` e `1`, `ALLOW_SNAPSHOT_ISOLATION`, agendamento com Elastic Jobs ou Azure Automation. "The verification process and the integrity of the database depend on the integrity of the input digests." https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-verification
- Alertas sobre uploads falhados e sobre queda de uploads. https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-monitor-digest-uploads
- Política de imutabilidade, estado `locked`, `allowProtectedAppendWrites`, a conta não se apaga com política `locked`. https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview
- Confidential Ledger: entrada de 8 KiB, 2 ledgers por subscrição, eliminação irreversível. https://learn.microsoft.com/en-us/azure/confidential-ledger/limits
- Elastic Jobs não suportado com Entra-only. https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication
- `isLedgerOn` não se altera depois da criação. https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/databases
- Estado GA do ledger em Azure SQL Database: NÃO VERIFICADO como frase literal. As páginas mostram "Applies to" sem marca de preview. https://learn.microsoft.com/en-us/azure/azure-sql/database/ledger-landing

## Estado

Proposto. Decisões humanas por tomar antes de qualquer lock: `isLedgerOn`, retenção, pseudonimização e destino dos digests.
