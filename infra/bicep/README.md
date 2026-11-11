# Infraestrutura Bicep (Fase C, ambiente de teste)

## Estado real

Este Bicep nunca foi aplicado. Não existe subscrição Azure associada ao projecto.

Sintaxe não validada por compilador. Nem `bicep` nem `az bicep` existem na máquina onde estes ficheiros foram escritos e não se instalou nada. O primeiro passo antes de qualquer outro é correr `bicep build main.bicep` e corrigir o que o compilador apontar.

Os tipos de recurso, as apiVersions e as propriedades foram retirados da pesquisa em Microsoft Learn de 2026-11-11. As versões mais recentes (2026-xx) estão documentadas, mas não se sabe se a versão instalada do Bicep ou todas as regiões as aceitam. Se o compilador ou o deploy as recusarem, a pesquisa lista versões estáveis anteriores para cada tipo, mas as propriedades só foram confirmadas na versão que está em uso aqui.

Tudo o que não está verificado aparece como comentário `// POR VALIDAR:` junto da linha em causa. Não há valores inventados: o que não se sabe fica em comentário ou em parâmetro sem valor por omissão.

Fases C, D e E não foram executadas. Não existe adaptador real para Entra ou Graph. Este Bicep cria a infraestrutura onde esses adaptadores correrão, e não os substitui.

## Porque não aplicar sem os gates da Fase A

A Fase A é o gate de aprovação de âmbito, licenças e permissões. Este Bicep toma decisões que dependem dela e que são difíceis ou impossíveis de desfazer.

| Decisão | Porquê não se desfaz |
| --- | --- |
| `isLedgerOn` na base de dados | Não pode ser alterado depois da criação. Com `true`, todas as tabelas passam a ser ledger. Uma base de dados ledger não volta a ser normal e uma tabela ledger não volta a ser normal. |
| Retenção da política de imutabilidade e o seu lock | Depois do lock a política não se apaga, o período só aumenta (máximo de cinco aumentos ao nível do contentor), e a conta de armazenamento não se elimina antes de a retenção expirar. |
| Protecção contra purga do Key Vault | Não aceita `false` depois de activada. O nome do cofre fica reservado durante a retenção após eliminação. |
| Autenticação só Entra no SQL | Desliga a autenticação SQL, incluindo para administradores. A conta gerada (`CloudSA...`) não serve de recurso nem de break-glass. |
| Redundância da conta de digests | LRS não é suportado para digests automáticos. |
| Dados pessoais | Não podem entrar em tabelas ledger nem no armazenamento imutável, porque não há forma documentada de os apagar. A decisão de pseudonimização, retenção e eliminação tem de existir antes do lock. |

Há ainda decisões de custo (níveis do SQL, APIM e Service Bus) e de rede (private endpoint, firewall, rede virtual) que este Bicep deixa em aberto de propósito.

Aplicar este Bicep também não prova nada sobre contenção. Uma implementação com êxito, um dashboard verde ou uma demonstração com adaptadores simulados não provam contenção. O gate da Fase C é a prova de impossibilidade de acesso directo no perfil forte, e esta infraestrutura sozinha não a dá (ver a nota sobre o ingress do executor).

## Estrutura

| Ficheiro | O que cria |
| --- | --- |
| `main.bicep` | Orquestra os módulos. Parâmetros: região, prefixo, tenant, administradores, imagem. Sem segredos e sem valores por omissão sensíveis. |
| `main.bicepparam.example` | Exemplo com marcadores. Copiar para `main.bicepparam` e preencher. |
| `modules/identities.bicep` | Três identidades user-assigned separadas: control-api, policy-executor, containment-worker. |
| `modules/monitoring.bicep` | Workspace Log Analytics e Application Insights ligado ao workspace. |
| `modules/keyvault.bicep` | Key Vault com autorização por RBAC, soft delete e protecção contra purga. Sem segredos e sem papéis. |
| `modules/storage-digests.bicep` | Conta de armazenamento e contentor `sqldbledgerdigests` com política de imutabilidade NÃO bloqueada. |
| `modules/sql.bicep` | Servidor SQL só Entra, base de dados e, se activado, a configuração do upload de digests. |
| `modules/servicebus.bicep` | Namespace sem SAS e fila `containment` com detecção de duplicados, dead-letter e `maxDeliveryCount`. |
| `modules/containerapps.bicep` | Ambiente e três container apps, cada uma com a sua identidade. Executor e control-api com ingress, worker sem ingress. |
| `modules/apim.bicep` | Instância APIM e uma API para o executor com `validate-azure-ad-token`. |
| `policies/executor-inbound.xml` | Modelo da política de entrada. O Bicep substitui os marcadores. |

### Identidades

A identidade de controlo (`control-api`) nunca é atribuída ao runtime do agente. Este Bicep não cria a identidade do runtime do agente. Essa identidade é criada à parte, sem acesso à base de dados, ao Service Bus ou ao Key Vault desta aplicação.

Não se reutiliza uma identidade entre serviços com âmbitos incompatíveis.

### Contentor de digests e política não bloqueada

A política criada tem o período indicado em `digestImmutabilityDays` e `allowProtectedAppendWrites` activo (os digests são append blobs). Fica sem lock de propósito. Uma política desbloqueada protege contra eliminação e substituição, mas pode ser encurtada ou apagada, e a documentação recomenda-a só para testes de curta duração.

O lock é um passo manual, depois de aprovada a retenção, pelas razões da tabela acima. O comando documentado é `az storage container immutability-policy lock`, com o `etag` da política obtido ao criá-la ou lê-la, e a opção `--if-match`. Não está confirmado que o lock seja declarável em Bicep. Confirmar a sintaxe completa na documentação actual antes de executar.

O valor `digestImmutabilityDays = 1` do ficheiro de exemplo vem do quickstart da documentação. A própria documentação avisa que em produção se deve usar um valor muito maior.

### Ingress do executor

O ingress do `policy-executor` está externo e sem restrição ao APIM. Com este Bicep, o executor é alcançável sem passar pelo gateway. O BT-05 (gateway e backend não podem ser contornados) não fica provado por esta infraestrutura. A mitigação mínima é o executor validar o token ele próprio, o que é uma propriedade do código e não do Bicep. A restrição de rede ao APIM é POR VALIDAR: a propriedade `ipSecurityRestrictions` existe, mas os seus campos não foram extraídos. Resolver na Fase C antes de declarar o perfil forte.

Além disso, o Microsoft Graph é um serviço público e nenhum mecanismo de rede do lado do cliente restringe o Graph a pedidos vindos do APIM. Quem tiver um token Graph válido chama o Graph directamente. O que impede o contorno é o agente nunca possuir a credencial Graph, que fica só no executor.

## Ordem de aplicação

1. Fechar os gates da Fase A e as decisões da tabela acima. Registar cada decisão num ADR.
2. Correr `bicep build main.bicep` e corrigir erros de sintaxe e de tipos. Confirmar as apiVersions contra o compilador.
3. Criar o grupo de recursos e as atribuições de papel de quem vai aplicar. Ambos ficam fora do Bicep.
4. Copiar `main.bicepparam.example` para `main.bicepparam`, preencher, e resolver cada `POR VALIDAR` que afecte os parâmetros escolhidos.
5. Correr uma simulação de deploy (what-if) e rever o resultado. Confirmar na documentação actual os comandos de deploy do Azure CLI antes de os executar.
6. Primeiro deploy, com `enableLedgerDigestUpload = false`. Dentro dele, a ordem resolve-se por dependências: monitorização e identidades, Key Vault, conta de digests, SQL, Service Bus, container apps, APIM.
7. Atribuições de papéis manuais (secção seguinte). Entre elas, o papel de escrita do servidor SQL na conta de digests.
8. Passos T-SQL pós-deploy: criar os utilizadores da base de dados para cada identidade, conceder só o mínimo, e activar `ALLOW_SNAPSHOT_ISOLATION` se ainda não estiver activo (necessário para as procedures de verificação).
9. Segundo deploy com `enableLedgerDigestUpload = true`. Verificar que os digests começam a aparecer (runbook 05).
10. Decidir e aprovar a retenção. Só então bloquear a política de imutabilidade (passo manual, irreversível).
11. Configurar os alertas de falha de upload de digests e de queda de uploads com sucesso. Os alertas não fazem parte deste Bicep.

## Atribuições e configurações que ficam de fora de propósito

Nenhuma atribuição de papel Azure está neste Bicep. O tipo de recurso das atribuições e os identificadores dos papéis não foram verificados na pesquisa, e atribuir papéis é uma decisão de privilégio que deve ser revista por pessoa.

| Item | Porque fica fora |
| --- | --- |
| Papel `Storage Blob Data Contributor` do servidor SQL (identidade de sistema) na conta de digests | Exigido pela documentação dos digests. Em geo-replicação ou grupos de failover, as réplicas secundárias precisam do mesmo papel. Atribuição manual, antes de ligar o upload. |
| Utilizadores T-SQL das identidades (`CREATE USER ... FROM EXTERNAL PROVIDER`) | É T-SQL, não é declarável em Bicep. Princípio do menor privilégio: identidade de escrita só com o necessário nas tabelas ledger, identidade de verificação só com `VIEW LEDGER CONTENT`, e nenhuma com `ENABLE LEDGER` em runtime. A sintaxe `GRANT` exacta de cada permissão de ledger não foi lida: confirmar na documentação actual. |
| Papéis de dados do Service Bus para as identidades | Nomes dos papéis não verificados. O namespace tem `disableLocalAuth`, por isso sem papel ninguém consegue enviar ou receber. |
| Papéis do Key Vault para os administradores | Nomes dos papéis não verificados. Os `adminObjectIds` para além do primeiro servem para esta atribuição manual. |
| `AcrPull` ou equivalente, se `registryServer` estiver definido | Cada app puxa a imagem com a sua identidade. A atribuição é manual. |
| Grant `Selected` no site SharePoint (e o consentimento da permissão no Entra) | O consentimento sozinho não dá acesso: é preciso atribuição no recurso. Essa atribuição é feita por uma identidade separada da do executor, com a permissão que a documentação exige para atribuir (`Sites.FullControl.All` ao nível do site), nunca pelo `policy-executor` nem pela `control-api`. Há uma contradição na documentação para lista e item, por isso o requisito mais forte é o que vale até ser testado em tenant. |
| Identidade do runtime do agente e os seus acessos | Criada à parte. Não recebe nenhum acesso aos recursos desta aplicação. |
| Autenticação do APIM para o executor | Não configurada. Quem edita políticas APIM pode usar a managed identity do serviço. Nunca usar a identidade do APIM como credencial de dados. |
| Job de verificação periódica da ledger | Com autenticação só Entra, Elastic Jobs não é suportado. A documentação indica Azure Automation. Fora do Bicep e a definir na Fase C. |
| Rede privada, firewall do SQL e da conta de armazenamento, private endpoint | Tipos não verificados e decisão da Fase A. Sem regra de firewall ou private endpoint, nenhum cliente liga ao SQL pela rede pública. O comportamento do upload de digests com rede restrita não foi verificado. |
| Variáveis para Service Bus, Key Vault e Application Insights nas apps | O contrato de configuração actual só lista as variáveis pedidas. Acrescentar quando o código as definir. |
| Alertas, Azure Policy, Defender, Sentinel, Purview | Fora do âmbito deste Bicep. |
| Contas de break-glass | Independentes desta aplicação, por definição (runbook 04). |

## Variáveis de ambiente das aplicações

Todas as apps recebem `Storage__Provider=SqlServer`, `Storage__ConnectionString`, `Storage__EnsureSchema=false`, `Auth__Authority`, `Auth__Audience`, `Auth__AllowedTenants__0` e `Adapters__Mode`. Cada app recebe também `AP_SERVICE` com o valor `control`, `executor` ou `worker`.

`Adapters__Mode` só pode ser `Simulation`: o código recusa arrancar com qualquer outro valor, porque não existem adaptadores reais para Entra nem para Graph. Aplicar este Bicep não dá contenção real, só dá a infra-estrutura onde a simulação corre. O Bicep nunca foi aplicado.

`Storage__EnsureSchema=false` porque o esquema (`src/Storage.SqlServer/Schema/001_schema.sql`) cria uma tabela ledger e isso exige `ENABLE LEDGER`, que nenhuma identidade de execução deve ter. O esquema aplica-se uma vez, com uma conta administrativa, antes do primeiro arranque. No código a omissão é `true`, por isso quem tirar esta variável volta a dar ao arranque a tentativa de criar o esquema.

A cadeia de ligação não contém palavra-passe: usa identidade gerida, com o `clientId` da identidade própria de cada serviço. O código passa-a tal e qual ao `Microsoft.Data.SqlClient`. A grafia exacta para identidade gerida é POR VALIDAR contra a documentação do driver e ensaiada contra Azure SQL na fase C.

`Auth__Audience`, `Auth__Authority` e `Auth__AllowedTenants__0` chegam também ao worker só porque o contrato comum os lista para os três serviços. O worker não lê nenhuma variável `Auth__` e não valida tokens, por isso podem sair sem efeito. O worker lê `Worker__PollSeconds` (omissão 2) e o executor lê `Execution__ExternalTimeoutSeconds` (omissão 10): nenhuma é definida aqui.

## O que está POR VALIDAR

Lista consolidada. Cada item tem o comentário correspondente junto do código.

| Área | Item |
| --- | --- |
| Compilação | Sintaxe e tipos não validados por compilador. apiVersions de 2026 não confirmadas contra o Bicep instalado. |
| Identidades | Propriedades só de leitura `principalId` e `clientId`. |
| SQL | `identity` do servidor; `administratorType` no bloco `administrators`; valores de `sku.name`; acesso de rede; uso de identidade user-assigned do servidor para o upload de digests; comportamento do upload com rede restrita. |
| Digests e armazenamento | Pré-criação do contentor `sqldbledgerdigests` com política antes do primeiro upload; lock da política em Bicep; upload com chaves partilhadas desligadas; tipo `blobServices` (evitado com nomes completos). |
| Service Bus | Grafia dos valores de `sku.name`; alteração de `requiresSession` depois da criação; sessões por nível. |
| Key Vault | `accessPolicies: []` com `enableRbacAuthorization: true`; valores de `publicNetworkAccess`. |
| Container Apps | Formato do bloco `identity`; `template.containers[]`; `listKeys()` do workspace; `ingress.fqdn`; restrição do ingress do executor; recursos (cpu e memória). |
| APIM | Tipos `service/apis` e `service/apis/policies` e propriedades; nomes e ordem dos elementos filhos de `validate-azure-ad-token`; operação `POST /actions` (em comentário). |
| Monitorização | Valores aceites de `retentionInDays`; semântica de `dailyQuotaGb`. |
| Autoridade de tokens | O código usa o emissor dos metadados de `Auth__Authority`. A versão do emissor dos tokens reais emitidos no tenant é por validar. |

## Natureza dos custos

Sem valores. A calculadora oficial é https://azure.microsoft.com/pricing/calculator/ e cada serviço tem a sua página de preços. A pesquisa confirmou que as páginas existem, mas não leu o conteúdo delas.

| Serviço | Natureza do custo | Página de preços |
| --- | --- | --- |
| Azure SQL Database | Computação (provisionada ou serverless, com pausa automática e capacidade mínima), armazenamento e redundância das cópias de segurança. As tabelas ledger não se purgam, logo o armazenamento só cresce. A verificação da ledger consome recursos da base. Custo específico da funcionalidade ledger: não verificado. | https://azure.microsoft.com/pricing/details/azure-sql-database/single/ |
| Blob Storage imutável | Capacidade, operações e redundância. Sem custo adicional de capacidade pela imutabilidade. Modificar ou bloquear a política conta como operação. Com política bloqueada a capacidade não se liberta antes do fim da retenção. | https://azure.microsoft.com/pricing/details/storage/blobs/ |
| Service Bus | Por nível. Premium tem preço fixo e dimensiona-se em unidades de mensagens. Standard paga-se por utilização. | https://azure.microsoft.com/pricing/details/service-bus/ |
| Key Vault | Por nível e por operações. Detalhe por operação não verificado em Microsoft Learn. | https://azure.microsoft.com/pricing/details/key-vault/ |
| Container Apps | Plano de consumo, em que só se paga o que as apps usam enquanto correm. Perfil dedicado paga instâncias alocadas e uma taxa de gestão. Este Bicep usa o plano de consumo. | https://azure.microsoft.com/pricing/details/container-apps/ |
| API Management | Por nível e por unidades. Costuma ser um custo fixo relevante nos níveis com unidades, mas a natureza por nível não foi lida em Microsoft Learn. | https://azure.microsoft.com/pricing/details/api-management/ |
| Log Analytics e Application Insights | Consumo. A ingestão de registos é normalmente a maior componente. Paga-se também retenção e exportação. Controlo por `retentionInDays` e `dailyQuotaGb`. | https://azure.microsoft.com/pricing/details/monitor/ |
| Managed identities | Natureza do custo não verificada. | Sem página verificada |

Licenças de Entra Agent ID, Agent 365 e Conditional Access para agentes não fazem parte deste Bicep e têm requisitos próprios, com contradições entre páginas oficiais. Ver a pesquisa de identidade.

## Fontes

Verificadas em Microsoft Learn a 2026-11-11.

- Referência de templates, base: https://learn.microsoft.com/en-us/azure/templates/
- Ledger, gestão de digests: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management
- Ledger, limites: https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits
- Autenticação só Entra no Azure SQL: https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication
- Blob imutável, políticas de contentor: https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-container-level-worm-policies
- Service Bus, detecção de duplicados: https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection
- Service Bus, dead-letter: https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues
- APIM, validate-azure-ad-token: https://learn.microsoft.com/en-us/azure/api-management/validate-azure-ad-token-policy
- Container Apps, faturação: https://learn.microsoft.com/en-us/azure/container-apps/billing
