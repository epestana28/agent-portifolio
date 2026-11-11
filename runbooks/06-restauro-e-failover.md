# 06. Restauro e failover

Estado deste runbook: escrito a partir do desenho e da documentação oficial do SQL Ledger, sem subscrição. Nunca ensaiado. O teste BT-17 só fica provado nas fases C e D. A documentação do processo de restauro e de failover do Azure SQL em si não foi lida nesta pesquisa: onde o texto diz "confirmar na documentação actual", não executar sem o confirmar.

## Objectivo

Recuperar a aplicação depois de um restauro da base de dados ou de um failover sem reabrir tráfego antes de reconciliar, sem que um agente suspenso volte a estar activo, e com o RPO medido e validado.

Três regras que valem durante todo o runbook:

1. Não reabrir tráfego antes de reconciliar.
2. Um agente suspenso não pode voltar a activo por causa do restauro. A reactivação só se faz pelo runbook 03.
3. A base restaurada pode estar atrasada em relação ao mundo real. Tudo o que o mundo real fez depois do ponto de restauro tem de ser descoberto, não presumido.

## Quem pode executar

Responsável de operações (quem gere a base e a infraestrutura), com um `PolicyAdmin` presente para decidir sobre agentes e um `Auditor` para a verificação da ledger. Reabrir tráfego exige a aprovação de um `PolicyAdmin` e do responsável de segurança.

## Pré-requisitos

1. Incidente declarado, ticket e responsável de comunicação.
2. RPO e RTO aprovados antes do incidente. Este runbook não fixa valores.
3. Registos independentes da base de dados disponíveis: auditoria e registos de início de sessão do Entra, registos do APIM, registos do Service Bus, tickets de suspensão, e as cópias fora da aplicação do inventário de agentes e grants.
4. Os runbooks 01, 03, 04 e 05 à mão.
5. Saber como manter o tráfego fechado: parar a entrada no gateway ou o executor, pelo método aprovado. O comando concreto depende do runtime e não foi verificado: confirmar na documentação actual.

## Passos

1. Manter ou fechar o tráfego. O executor não admite acções e a control-api não aceita operações até ao passo 11. O worker não processa mensagens até ao passo 9. Registar a hora UTC em que o tráfego ficou fechado.
2. Executar o restauro ou o failover conforme o procedimento aprovado. Confirmar na documentação actual do Azure SQL para o cenário (restauro pontual, geo-restauro, grupo de failover). Registar o ponto de restauro, a hora do pedido e a hora de conclusão.
3. Perceber o efeito na ledger. A documentação diz que cada restauro dá à base uma nova hora de criação e que a verificação usa os digests de todas as incarnations. Em grupos de failover, os dois lados têm o mesmo caminho de digest, e o caminho não muda no failover. Se o grupo de failover for eliminado ou o link removido, a réplica secundária passa a primária com uma pasta `RemovedSecondaryReplica` no caminho. Com geo-replicação, só se emitem digests de dados já replicados nos secundários.
4. Confirmar os digests e as permissões depois do restauro. A identidade do servidor SQL tem de ter o papel de escrita na conta de digests, e as réplicas secundárias precisam do mesmo papel. Se a base restaurada for um servidor diferente, a configuração de upload tem de ser refeita.
5. Verificar a ledger com o runbook 05 antes de qualquer outra coisa. Resultado negativo: parar e tratar como incidente de segurança, porque pode ser adulteração e não consequência do restauro.
6. Medir o RPO. Obter a hora do último commit na base restaurada (`MAX(commit_time)` em `sys.database_ledger_transactions`) e compará-la com:
   - a hora do último digest no armazenamento (o digest cobre dados já replicados, pelo que pode ser mais recente que a base restaurada se o restauro foi para trás no tempo);
   - os registos independentes: última acção do executor nos registos do APIM, última mensagem enviada ao Service Bus, últimos eventos do Entra e do SharePoint.
   A diferença é a janela de perda. Registar o valor e comparar com o RPO aprovado. Se for maior, escalar antes de continuar.
7. Reconciliar o estado dos agentes. Para cada agente, comparar o estado da base restaurada com a evidência independente:
   - Qualquer agente que tenha sido suspenso na janela de perda (ticket, mensagem de contenção, desactivação no Entra) e apareça como `Active`: suspender de imediato (runbook 01) antes de qualquer reabertura.
   - Em caso de dúvida sobre um agente, suspender. O custo de uma suspensão a mais é menor que o de uma acção indevida.
   - O estado das identidades e dos grants no Entra e nos recursos não foi afectado pelo restauro: o que foi desactivado ou retirado lá continua assim. Confirmar com o inventário fora da aplicação.
   - Um agente suspenso não pode voltar a `Active` por este procedimento. Reactivar só pelo runbook 03.
8. Tratar as capacidades. Uma base restaurada para trás no tempo pode ter `revocationEpoch` inferior ao que existia antes da falha, e capacidades revogadas na janela de perda podem aparecer como válidas. Inferência a validar na Fase C: tratar como não fiáveis as capacidades e as epochs dessa janela. A medida segura é suspender o agente (que incrementa a epoch) e reactivá-lo pelo runbook 03, com nova epoch e capacidades reemitidas. Não existe um endpoint de reposição em massa nesta documentação.
9. Tratar o Service Bus. Mensagens de epoch diferente da actual são descartadas como "superseded" e nunca se executam. Rever a dead-letter queue (não se limpa sozinha): cada mensagem lá é decidida por pessoa. A detecção de duplicados só cobre o envio dentro da janela, pelo que o consumidor tem de continuar idempotente. Só depois de rever, ligar o worker.
10. Reconciliar acções externas. Acções que o executor admitiu e enviou ao Microsoft Graph na janela de perda podem ter efeito sem registo. Procurar nos registos independentes. Acções sem registo mas com efeito ficam registadas como `Unknown` e são reconciliadas. Não existe exactly-once universal para efeitos Graph.
11. Reconciliar os grants: comparar os grants observados com os registados. Grant amplo (por exemplo `Sites.Read.All`, `Files.Read.All`) é drift perigoso e suspende o agente.
12. Reabrir tráfego de forma gradual e só depois da aprovação: primeiro a control-api e o worker, depois o executor. Antes de admitir acções do agente, correr os testes negativos de contenção: suspensão recusa, recurso não autorizado falha, capacidade antiga recusada.
13. Observar durante um período combinado e rever os alertas de ledger, digest, drift e acção pós-suspensão.
14. Fechar o incidente com o relatório do RPO real, das reconciliações e das acções manuais.

## Como verificar

1. A verificação da ledger passou (runbook 05) depois do restauro, incluindo digests de todas as incarnations.
2. Nenhum agente que estivesse suspenso está `Active`. Lista comparada com a evidência independente.
3. O RPO medido está dentro do aprovado, ou a diferença está registada com decisão de aceitação.
4. Nenhuma acção de agente foi admitida durante a janela de tráfego fechado. A auditoria não mostra `action.admitted` entre a hora de fecho e a de reabertura.
5. A dead-letter queue foi revista.
6. Os testes negativos do passo 12 passaram antes de reabrir.

## Evidência a registar

- Linha do tempo UTC: falha, decisão, tráfego fechado, restauro, verificação, reconciliação, reabertura.
- Ponto de restauro, hora do último commit restaurado, hora do último digest, janela de perda e comparação com o RPO.
- Resultado da verificação da ledger.
- Lista de agentes comparada com a evidência independente, e os agentes suspensos de novo.
- Acções externas com resultado `Unknown` e a sua reconciliação.
- Mensagens da dead-letter queue e a decisão sobre cada uma.
- Quem aprovou a reabertura.

## Limites conhecidos

- O processo de restauro e de failover do Azure SQL, e o RPO e o RTO reais, não foram verificados. Só se ensaiam em Azure (fases C e E).
- Os digests só cobrem dados replicados em cenários geográficos e a ledger não consegue reconstruir o que se perdeu. A ledger prova adulteração, não recupera nem garante disponibilidade.
- O restauro não repõe estado externo no Entra nem nos recursos. Em sentido contrário, não desfaz contenção externa.
- Digests e retenção: com política bloqueada não se apagam blobs antes do fim da retenção, e a conta de armazenamento não se elimina enquanto houver contentor com política bloqueada. Isto condiciona qualquer plano de reconstrução do ambiente.
- Não há exactly-once universal. Acções externas aceites antes da falha podem completar-se depois.
- Esta recuperação não é automática. As reconciliações dependem de evidência independente, e se ela não existir o runbook obriga a suspender e rever à mão.

## Documentação oficial (verificada a 2026-11-11)

- Digests, restauro e failover (incarnations, grupos de failover): https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management
- Verificação da ledger: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-verification
- Recuperar depois de adulteração: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-how-to-recover-after-tampering
- Limites da ledger: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits
- Service Bus, dead-letter: https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues
- Service Bus, detecção de duplicados: https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection
