# Natureza dos custos e decisões de tier

Estado: rascunho por validar no tenant

Verificado em documentação oficial a 2026-11-11

## 1. Âmbito e regra deste documento

Este documento responde à exigência da especificação (secção 17) de justificar tiers, rede, suporte e custos antes de provisionar. Não contém valores monetários nem estimativas numéricas. Isso é intencional.

Sem subscrição nem tenant, qualquer número seria inventado. Para cada serviço, o documento descreve a natureza do custo (o que se paga), o que o faz crescer, a decisão de tier por tomar e a página oficial de preços. O valor sai da página oficial de preços e da calculadora oficial (`https://azure.microsoft.com/pricing/calculator/`), depois de as decisões abaixo estarem tomadas.

Estado do projecto, sem exagero: fase A em curso (este documento), fase B implementada em local sem credenciais reais, fases C, D e E não executadas. O Bicep está escrito e nunca foi aplicado. Nada foi provisionado, por isso não existe custo observado.

Legenda: Verificado (texto lido em Microsoft Learn a 2026-11-11), Inferência (conclusão a partir de factos verificados) e NÃO VERIFICADO (a documentação lida não o afirma). Os URL das páginas de preços responderam com sucesso a 2026-11-11, mas o seu conteúdo não foi lido. Delas não se tira nenhum valor neste documento.

## 2. Quadro resumo

| Serviço | O que se paga | Decisão de tier por tomar | Página oficial de preços |
| --- | --- | --- | --- |
| Azure SQL Database | Computação, armazenamento e redundância de backup | Modelo de compra, computação provisioned ou serverless, redundância de zona e de backup | `https://azure.microsoft.com/pricing/details/azure-sql-database/single/` |
| Storage com imutabilidade | Capacidade, operações e redundância | Redundância (não LRS), versioning, período de retenção antes do lock | `https://azure.microsoft.com/pricing/details/storage/blobs/` |
| Service Bus | Tier fixo ou variável conforme o tier | Standard ou Premium (Basic não tem detecção de duplicados) | `https://azure.microsoft.com/pricing/details/service-bus/` |
| Key Vault | Operações e tier | standard ou premium | `https://azure.microsoft.com/pricing/details/key-vault/` |
| Container Apps | Recursos usados, ou instâncias alocadas por perfil | Consumption ou Dedicated, réplicas mínimas e máximas | `https://azure.microsoft.com/pricing/details/container-apps/` |
| API Management | Tier e unidades implantadas | Tier, número de unidades, modo de rede | `https://azure.microsoft.com/pricing/details/api-management/` |
| Azure Monitor, Log Analytics, Application Insights | Ingestão de dados, retenção e exportação | Plano do workspace, retenção, quota diária, destino dos logs | `https://azure.microsoft.com/pricing/details/monitor/` |
| Confidential Ledger (opção) | NÃO VERIFICADO | Usar ou não, como destino de digests | `https://azure.microsoft.com/pricing/details/confidential-ledger/` |
| Licenças Entra, Agent 365, Workload Identities Premium | Por utilizador ou por licença atribuída | Que licenças, para quantos utilizadores | Agent 365: `https://www.microsoft.com/microsoft-agent-365`. Entra: página de preços não verificada |

## 3. Azure SQL Database

### Natureza do custo

Verificado. A documentação descreve dois modelos de compra: vCore ("provides a choice between the provisioned or serverless compute tiers") e DTU. Em computação provisioned paga-se a computação sempre alocada. Em serverless a computação escala e pode pausar, com `autoPauseDelay` e `minCapacity` como propriedades de controlo. Armazenamento e redundância de backup são dimensões separadas (`requestedBackupStorageRedundancy`: Geo, GeoZone, Local, Zone). Fonte: referência Bicep de `Microsoft.Sql/servers/databases` em `https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/databases`.

Custo específico da funcionalidade ledger: NÃO VERIFICADO. A documentação lida não o indica.

### O que o faz crescer

- Tamanho do armazenamento. Inferência a partir de factos verificados: as tabelas ledger não permitem apagar dados antigos nem `TRUNCATE`, e objectos "eliminados" ficam fisicamente na base. Fonte: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits`. Com `isLedgerOn` ligado, todas as tabelas passam a ser ledger e as actualizáveis têm tabela de histórico. O armazenamento só cresce.
- Verificação da ledger. A documentação diz que pode ser um processo que consome muitos recursos em bases grandes. A frequência fica a cargo do cliente. Fonte: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-verification`.
- Redundância de zona e de backup, e réplicas geográficas. Se houver secundários, os digests só se emitem para dados já replicados.
- Agendamento da verificação. Com autenticação Entra-only, Elastic Jobs não é suportado. A opção documentada que resta é Azure Automation, que é outro serviço com a sua própria facturação. Fonte: `https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication`.

### Decisão de tier por tomar

- Modelo de compra e computação (provisioned ou serverless). Os valores de `sku` aceites não estão enumerados na referência lida (NÃO VERIFICADO). O exemplo da documentação usa `GP_Gen5_2` em T-SQL.
- `isLedgerOn` ao nível da base ou ledger tabela a tabela. É irreversível: "the value of this property cannot be changed after the database has been created". Tem efeito directo no crescimento do armazenamento.
- Redundância de zona e redundância de armazenamento de backup.
- Interacção entre pausa automática da computação serverless e o upload de digests e a verificação: NÃO VERIFICADO.

## 4. Storage com imutabilidade

### Natureza do custo

Verificado. "There's no extra capacity charge for using immutable storage. Immutable data is priced in the same way as mutable data." Com imutabilidade ao nível de versão, "the bill might be higher because you enabled versioning". Modificar uma política de retenção (bloquear ou estender) resulta numa cobrança de operações "Other operations". Fonte: `https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview`.

Paga-se capacidade, operações e redundância.

### O que o faz crescer

- Número e tamanho dos blobs de digests. Verificado: com gestão automática, o digest gera-se a cada 30 segundos se houver transacções. Inferência: o volume cresce com a actividade da base. Fonte: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management`.
- Retenção. Verificado: com a política locked, a capacidade não pode ser libertada antes do fim da retenção, e a conta de armazenamento não pode ser apagada enquanto houver contentor com política locked ou blobs presentes. O custo da capacidade fica comprometido pelo período escolhido.
- Redundância. Verificado: a gestão automática de digests não aceita contas LRS. Fonte: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits`.
- Versioning, se for escolhida imutabilidade ao nível de versão.

### Decisão de tier por tomar

- Redundância da conta (qualquer uma excepto LRS) e acesso.
- Imutabilidade ao nível de contentor ou de versão. Para os digests, a documentação usa política de contentor com `allowProtectedAppendWrites`.
- Período de retenção antes do lock. Verificado: depois do lock só pode aumentar, no máximo cinco vezes. É também uma decisão de custo, porque a capacidade fica comprometida.
- Quem executa o lock e quando. Não está confirmado que o lock se declare em Bicep.

## 5. Service Bus

### Natureza do custo

Verificado. Por tier. A documentação indica Premium com preço fixo ("Fixed pricing") e Standard com preço variável conforme o consumo ("Pay as you go variable pricing"). Premium dimensiona-se em messaging units (`sku.capacity`). Detecção de duplicados exige Standard ou Premium. Fontes: `https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection` e `https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-azure-and-service-bus-queues-compared-contrasted`.

### O que o faz crescer

- Volume de mensagens e operações, no tier Standard.
- Unidades de mensagens, no tier Premium.
- Dead-letter queue. Verificado: não tem limpeza automática. As mensagens ficam até serem lidas e completadas. Fonte: `https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues`.
- Detecção de duplicados com janela larga. Verificado: "Enabling duplicate detection and the size of the window directly impact the queue (and topic) throughput".
- Estado de sessões. Verificado: conta para a quota de armazenamento da entidade.

### Decisão de tier por tomar

- Standard ou Premium. Basic não serve porque não tem detecção de duplicados.
- Janela de detecção de duplicados (por omissão 10 minutos, entre 20 segundos e 7 dias) e `maxDeliveryCount` (por omissão 10).
- Sessões por tier: disponibilidade NÃO VERIFICADA.
- Rede privada para Service Bus e o tier que a permite: NÃO VERIFICADO.
- Zona redundante. Verificado: activar `zoneRedundant` cria um namespace Premium em regiões com zonas de disponibilidade.

## 6. Key Vault

### Natureza do custo

Verificado. Por tier (`standard` ou `premium`). A visão geral remete para a página de preços para "pricing and feature comparisons between tiers". O detalhe por operação não foi lido em Microsoft Learn (NÃO VERIFICADO).

### O que o faz crescer

- Número de operações sobre segredos, chaves e certificados.
- Inferência: se o desenho evitar segredos, com managed identities e federação, o uso é só para exceções. A especificação (secção 13) restringe o Key Vault a exceções que exigem segredos.
- Soft delete e purge protection. Verificado: `enableSoftDelete` não volta a false depois de true, e `enablePurgeProtection` é irreversível. `softDeleteRetentionInDays` aceita de 7 a 90. Um cofre apagado ocupa o nome durante a retenção.

### Decisão de tier por tomar

- standard ou premium. A diferença funcional entre os tiers não foi lida nesta pesquisa.
- Se o piloto precisa de cofre, ou se as identidades sem segredo bastam.
- Rede privada do cofre (`publicNetworkAccess`).

## 7. Container Apps

### Natureza do custo

Verificado. "Consumption plan: Serverless compute option where you're only billed for the resources your apps use as they're running." "Dedicated plan: Customized compute options where you're billed for instances allocated to each workload profile." Com perfil dedicado há uma "Dedicated plan management fee". Fonte: `https://learn.microsoft.com/en-us/azure/container-apps/billing`.

### O que o faz crescer

- Recursos consumidos enquanto as réplicas correm, no plano Consumption.
- Instâncias alocadas por perfil de carga, no plano Dedicated.
- Número de réplicas. Verificado: `maxReplicas` tem por omissão 10 se não for definido. Como `minReplicas` afecta a facturação em cada plano: NÃO VERIFICADO.
- Ingestão de logs, quando o destino de logs do ambiente é Log Analytics. Ver secção 9.

### Decisão de tier por tomar

- Consumption ou Dedicated. Os valores de `workloadProfileType` não estão enumerados na referência lida.
- Uma app única com os três serviços (a imagem do projecto tem um só artefacto com ControlApi, PolicyExecutor e ContainmentWorker) ou uma app por serviço. O isolamento de identidades de execução por serviço pesa na decisão.
- Réplicas mínima e máxima.
- Rede do ambiente (`vnetConfiguration`) e redundância de zona.

## 8. API Management

### Natureza do custo

Verificado. Por tier, com o `sku.name`: Consumption, Developer, Basic, BasicV2, Standard, StandardV2, Premium, Isolated. Por unidades implantadas (`sku.capacity`). O Consumption exige capacidade 0. Fonte: `https://learn.microsoft.com/en-us/azure/templates/microsoft.apimanagement/2024-05-01/service`.

A natureza do custo por tier (componente fixo, componente por chamada) não foi lida em Microsoft Learn (NÃO VERIFICADO). A página de preços resolve isto. A decisão não se toma sem ela.

### O que o faz crescer

- Tier escolhido e número de unidades.
- Rede. Verificado: integração de saída em rede virtual está documentada para os tiers v2. Private endpoint de entrada limita o tráfego de entrada e desactiva o acesso público. Os tiers que suportam cada modo de rede: NÃO VERIFICADO.
- Capacidades do AI gateway. Verificado: "Capability availability varies by API Management service tier". Fonte: `https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities`.
- Registo de pedidos e respostas, que alimenta o custo de Azure Monitor (secção 9).

### Decisão de tier por tomar

- Tier que suporte as políticas necessárias (`validate-azure-ad-token`, restrição de backend) e o modo de rede decidido.
- Número de unidades e redundância.
- Justificar o APIM face ao valor que traz neste produto. Inferência a partir da página do AI gateway: o APIM não cobre autorização fina de acesso ao Graph. O controlo real é o executor deter a credencial. O APIM entra pela regra Microsoft-first da especificação (secção 3) e pela validação de token à entrada. É uma decisão de custo e de valor a aprovar.

## 9. Azure Monitor, Log Analytics e Application Insights

### Natureza do custo

Verificado. "Azure Monitor uses a consumption-based pricing (pay-as-you-go) billing model where you only pay for what you use." "Log data ingestion is the largest component of Azure Monitor charges for most customers." Paga-se ingestão, retenção e exportação. Fonte: `https://learn.microsoft.com/en-us/azure/azure-monitor/fundamentals/cost-usage`.

### O que o faz crescer

- Volume de logs ingeridos. Fontes: logs de Container Apps (quando o destino é Log Analytics), logs de APIM, logs do executor e do worker, métricas de digests e de verificação, diagnósticos de SQL.
- Retenção (`retentionInDays`, "Allowed values are per pricing plan").
- Application Insights. Verificado: na versão `2020-02-02` de `Microsoft.Insights/components`, `WorkspaceResourceId` é obrigatório, por isso o Application Insights escreve num workspace Log Analytics e partilha a sua facturação.
- Telemetria do Agent 365 não passa por Azure Monitor. Tem licenciamento próprio (secção 11).

### Decisão de tier por tomar

- Plano do workspace. Os valores aceites são `CapacityReservation`, `Free`, `LACluster`, `PerGB2018`, `PerNode`, `Premium`, `Standalone` e `Standard`. Qual usar: por decidir.
- Retenção por tabela e destino de longo prazo.
- Quota diária (`workspaceCapping.dailyQuotaGb`). O efeito de atingir a quota sobre os dados que alimentam alertas de segurança: NÃO VERIFICADO. Não usar a quota como controlo de custo em telemetria de alertas sem decidir esse efeito.
- Destino dos logs do ambiente de Container Apps: `log-analytics`, `azure-monitor` ou `none`.
- Lembrete da especificação (secção 12): Application Insights e Monitor não substituem a ledger.

## 10. Confidential Ledger (opção)

### Natureza do custo

NÃO VERIFICADO em Microsoft Learn. Não consta nas páginas lidas. A página oficial de preços é `https://azure.microsoft.com/pricing/details/confidential-ledger/`. O caminho `/azure-confidential-ledger/` respondeu 404.

### O que o faz crescer

Não verificado. O que a documentação diz sobre limites, e que pode influenciar o desenho e o custo indirecto:

- 2 ledgers por subscrição por omissão e entradas de 8 KiB no máximo. Fonte: `https://learn.microsoft.com/en-us/azure/confidential-ledger/limits`.
- Replicação obrigatória para o par regional, que não se pode alterar nem desactivar. Fonte: `https://learn.microsoft.com/en-us/azure/confidential-ledger/secure-confidential-ledger`.
- O tipo (private ou public) não muda depois da criação. A eliminação é "hard delete".

### Decisão por tomar

- Usar ou não como destino de digests. A documentação apresenta-o como "stronger integrity guarantee for customers who might be concerned about privileged administrators access to the digest" (`https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management`). Inferência: justifica-se se o modelo de ameaça incluir administradores da subscrição ou do fornecedor com acesso ao armazenamento de digests.
- Baseline da especificação: Blob imutável com política locked. O Confidential Ledger é avaliação opcional (secção 11).
- A tabela comparativa entre Blob imutável e Confidential Ledger, referida pela página de digests, não foi localizada: NÃO VERIFICADO.

## 11. Licenças: Entra, Agent 365 e Workload Identities Premium

### Natureza do custo

Verificado. As licenças são por utilizador ou por licença atribuída, não por recurso Azure.

- Agent 365: "licensed on a per-user basis". Pelo menos um utilizador tem de ter licença qualificante atribuída. Está incluído no Microsoft 365 E7 e disponível como complemento para E5, A5 e Business Premium. Fonte: `https://learn.microsoft.com/en-us/microsoft-agent-365/overview`.
- Plataforma Entra Agent ID: "available for all Microsoft Entra customers". Fonte: `https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id`.
- Conditional Access para agentes: Microsoft 365 E7, ou Agent 365 com pelo menos Entra P1 ou Microsoft 365 E3. Outra página diz só Entra ID P1. A contradição está assinalada em `environment-assessment.md`.
- Conditional Access para service principals: Workload Identities Premium. Fonte: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity`.
- ID Protection para agentes: P2 durante a preview, e passará a exigir Agent 365 (sem data).
- Preços: NÃO VERIFICADO. As páginas de Microsoft Learn remetem para `https://www.microsoft.com/microsoft-agent-365`, fora de Learn. A página de preços do Entra e da Workload Identities Premium não foi verificada nesta pesquisa. Localizá-la antes de usar.

### O que o faz crescer

- Número de utilizadores a licenciar, por licença.
- Escolha entre E7 (agrega Agent 365 e Entra Suite) e complemento a uma licença existente.
- Inferência: um tenant de teste separado pode exigir licenças próprias.

### Decisão por tomar

- Se o piloto precisa de Conditional Access para agentes, ou se a contenção se faz só com desactivação e remoção de grants. Isso muda quais licenças são obrigatórias.
- Se a telemetria de Agent 365 entra no piloto. Sem licença atribuída, os spans são rejeitados com `200 OK`.
- Aceitar ou evitar dependência de funcionalidades em preview (ID Protection para agentes, condição "Agent execution environments"). Especificação, secção 0, ponto 8.

## 12. Outros itens sem custo verificado

- Managed identities: natureza do custo NÃO VERIFICADA (não consultada).
- Azure Automation, se for escolhido para agendar a verificação da ledger: facturação própria, não analisada.
- Redes privadas e private endpoints (APIM, Service Bus, Key Vault, SQL, Storage): custo e requisitos de tier NÃO VERIFICADOS.
- Defender, Purview e Sentinel: complementos opcionais da especificação (secção 12). Sujeitos a licenciamento e cobertura. Sem análise de custo nesta fase.
- Ambiente de produção e de teste separados: duplicam os serviços acima.

## 13. Perguntas a responder antes de provisionar

1. Qual é o orçamento aprovado para o piloto e quem o aprova?
2. O piloto corre em tenant e subscrição de teste dedicados, ou num existente? Se for dedicado, que licenças esse tenant tem de ter?
3. Que região e que região par serão usadas, e todos os serviços da lista estão disponíveis nessas regiões?
4. Qual é a classificação dos dados do site piloto, e que dados pessoais ficam excluídos da ledger e do armazenamento imutável?
5. `isLedgerOn` fica ao nível da base ou tabela a tabela? A decisão é irreversível e define o crescimento do armazenamento.
6. Qual é o período de imutabilidade antes do lock, aprovado por quem, e com que impacto aceite na capacidade comprometida?
7. Que redundância de conta de digests (não LRS) e de backup da base é aprovada?
8. Como e com que frequência se verifica a ledger, dado que Elastic Jobs não é suportado com Entra-only? Azure Automation ou outra opção?
9. O Confidential Ledger entra no piloto? O modelo de ameaça inclui administradores com acesso ao armazenamento de digests, e a replicação obrigatória para o par regional é aceitável?
10. Service Bus Standard ou Premium? Que janela de detecção de duplicados e que `maxDeliveryCount`? Quem esvazia a dead-letter queue?
11. Container Apps em Consumption ou Dedicated? Uma app ou uma por serviço? Réplicas mínima e máxima?
12. O APIM é necessário no piloto? Se sim, que tier, que unidades e que modo de rede, e o valor que traz justifica o custo face a validar o token no executor?
13. Que plano de workspace Log Analytics, que retenção e que política de quota diária, sem perder telemetria que alimenta alertas de segurança?
14. O Key Vault é necessário, ou as identidades sem segredo cobrem o piloto? Que tier?
15. Que licenças Entra e Agent 365 são necessárias para o âmbito do piloto, para quantos utilizadores, e quem as aprova?
16. A contradição de licença de Conditional Access para agentes (Entra ID P1 contra Agent 365) está resolvida no tenant?
17. Que funcionalidades em preview o piloto aceita usar, com que alternativa documentada e com que aceitação formal do risco?
18. Qual é o custo máximo aceitável por mês durante o piloto, e que alertas de custo (budgets) se criam antes do primeiro recurso?
