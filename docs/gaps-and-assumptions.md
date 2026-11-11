# Lacunas e pressupostos

Projecto: Agent Portifolio. Documento da fase A. Data de verificação das fontes Microsoft: 2026-11-11.

Este documento lista o que a especificação e a documentação oficial não fecham. Serve para o gate da fase A: quem aprova deve saber o que fica por validar e o que fica bloqueado sem tenant.

Estado do projecto, sem exagero: a fase A foi escrita a partir de documentação oficial e sem tenant. A fase B (fatia vertical local) está implementada em .NET 8, sem credenciais reais, com o mundo Microsoft simulado. As fases C, D e E não foram executadas. O Bicep está escrito e nunca foi aplicado. Não existe adaptador real para Entra nem para Graph.

## 1. Convenções

Marcas usadas na secção 2:

- NÃO VERIFICADO: a documentação lida não o afirma. Não quer dizer que seja falso.
- CONTRADITÓRIO: duas páginas oficiais dizem coisas diferentes.
- INFERÊNCIA: conclusão do projecto a partir de factos verificados. A Microsoft não a afirma.
- POR VALIDAR NO TENANT: depende do tenant e não pode ser decidido por leitura.

Cada facto Microsoft leva uma chave `Mxx` que remete para a tabela de fontes no fim do documento (URL do Microsoft Learn e data 2026-11-11). Onde a coluna "Fonte" diz "pesquisa", o facto vem de uma pesquisa feita sem identificar página e não tem URL próprio: nesses casos a lacuna é a própria falta de fonte.

As datas `ms.date` e `updated_at` de algumas páginas foram lidas dos metadados. Nas páginas de Graph, Exchange e APIM o conversor usado não devolveu metadados, por isso a data de última actualização dessas páginas é NÃO VERIFICADA.

## 2. Lacunas da documentação Microsoft

Cada linha diz o que falta ou contradiz, e o impacto no projecto.

### 2.1 Identidade, revogação e tokens

| Id | Estado | O que a documentação diz ou omite | Impacto no projecto | Fonte |
|---|---|---|---|---|
| L-01 | NÃO VERIFICADO | O Entra Agent ID "is available for all Microsoft Entra customers", mas o texto lido nunca o declara GA. As APIs Graph de agent identity estão em `v1.0`. A inferência de produção a partir de `v1.0` não é afirmação da Microsoft. | O ADR 0009 não pode afirmar GA. A aceitação do risco de depender do Agent ID é decisão humana. | M60, M18 |
| L-02 | NÃO VERIFICADO | Nenhum exemplo mostra o corpo do pedido que desactiva uma agent identity ou um blueprint principal. A propriedade `accountEnabled` existe e a permissão mínima chama-se `AgentIdentity.EnableDisable.All`, o que torna `{"accountEnabled": false}` provável (inferência). | O adaptador real de desactivação não pode ser escrito como facto. Validar no tenant na fase D. | M17, M18 |
| L-03 | CONTRADITÓRIO | A referência Graph descreve `accountEnabled` como "no users are able to sign in to this app". As páginas de administração falam em "block its access and token issuance". | A referência Graph não confirma bloqueio de tokens app-only. Medir no tenant. | M18, M06 |
| L-04 | NÃO VERIFICADO | Nenhuma página lida diz o que acontece aos access tokens já emitidos a uma agent identity depois de a desactivar. Toda a linguagem é sobre emissão futura. | É o centro de BT-14. O projecto assume validade até à expiração e mede o resultado real. Ver risco R-02. | M05, M06, M13 |
| L-05 | NÃO VERIFICADO | O efeito em cascata da desactivação da agent identity sobre o agent user associado. A documentação diz que o agent user "can only authenticate by presenting a token issued to the associated agent identity", o que sugere dependência, sem a afirmar. | Revogar um agente que use os três modos (aplicação, em nome do utilizador, agent user) pode exigir actuar em três sujeitos de token. | M21, M05 |
| L-06 | NÃO VERIFICADO | Se o bloqueio no centro de administração Microsoft 365 (Agent 365) altera `accountEnabled` no Entra, ou se são dois interruptores independentes. As três páginas de bloqueio foram lidas só por excerto. | Há possivelmente dois planos de controlo sem sincronização documentada. O adaptador e o relatório de cobertura têm de os tratar separadamente. | M64 |
| L-07 | NÃO VERIFICADO | Existe "Disable in all tenants" para blueprints multitenant no centro de administração. O endpoint Graph e a propriedade equivalente não estão documentados nas páginas lidas. | Fora do piloto de um tenant. Qualquer acção sobre blueprint multitenant fica sem automação até haver fonte. | M05 |
| L-08 | CONTRADITÓRIO | Uma página fala em desactivar o "agent identity blueprint". Outra diz que a mesma lista mostra blueprint principals e chama à acção "Disable an agent identity blueprint principal". A permissão `EnableDisable` existe para o principal e não há equivalente para o blueprint. | Não é claro se o disable tenant-local actua na application ou no service principal. O ADR 0002 trata o principal como alvo provável (inferência). | M05, M20 |
| L-09 | NÃO VERIFICADO | CAE para agentes: a única menção é "Some resources also support Continuous Access Evaluation". Sem lista de recursos nem de eventos, e sem dizer se desactivar uma agent identity é evento de revogação. | Não presumir revogação quase em tempo real para agentes. Ver R-02. | M04 |
| L-10 | CONTRADITÓRIO | CAE para workload identities diz "instantly" na introdução e não dá valor numérico. Os eventos críticos de utilizadores indicam "up to 15 minutes". CAE para workload identities só cobre Microsoft Graph, service principals single-tenant de aplicações de linha de negócio, exige `cp1` e exclui managed identities. | Não prometer revogação instantânea. Sem prazo numérico para service principals. | M02, M03 |
| L-11 | CONTRADITÓRIO | Duração por omissão do access token: "1 hour" em duas páginas, "60-90 minutes (75 minutes on average)" em duas, "two hours" para Teams e Microsoft 365 sem Conditional Access. A página `access-tokens` não se aplica a APIs Microsoft como o Graph. Duração para agent identities: NÃO VERIFICADO. | Usar 90 minutos como pior caso sem CAE. Com CAE, 28 horas (utilizadores) e 24 horas (workload identities). | M01, M02, M07, M08 |
| L-12 | NÃO VERIFICADO | Tokens já emitidos depois de `accountEnabled=false` num service principal: a página diz que impede a emissão e não diz mais. Só a página de CAE de workload identities liga "service principal disable" a tokens existentes, e só nas condições restritas. Para a desactivação de application (`isDisabled`) o texto é explícito: tokens existentes válidos até expirar. | Por analogia (inferência), tokens mantêm-se até expirar. Medir no tenant. | M11, M12, M03 |
| L-13 | NÃO VERIFICADO | Se `accountEnabled=false` é suportado e eficaz no service principal de uma managed identity. Não há procedimento de disable equivalente para managed identities. A cache de tokens é de cerca de 24 horas sem refresh forçado, e tokens de uma identidade eliminada valem até ao fim. | A managed identity tem a janela de revogação mais longa e menos controlos documentados. A documentação recomenda federar o blueprint numa managed identity, e o efeito da cache sobre a revogação de um blueprint federado é NÃO VERIFICADO. Ver R-03. | M09, M10, M14 |
| L-14 | CONTRADITÓRIO | Licença para Conditional Access aplicado a agentes: uma página exige Entra ID P1, a outra (mais recente) exige Microsoft 365 E7 ou Agent 365 mais Entra P1 ou Microsoft 365 E3. ID Protection para agentes: P2 durante o preview, e "starting soon" Agent 365. Preços: NÃO VERIFICADO (as páginas remetem para fora do Learn). | Registar Agent 365 como requisito até prova em contrário. Não escrever valores. Ver R-04. | M04, M05, M06, M62 |
| L-15 | NÃO VERIFICADO | As políticas de Conditional Access para agentes e os logs de sign-in de agentes usam o endpoint `beta` nos exemplos. Se as condições `includeAgentIdServicePrincipals`, `AllAgentIdUsers` e `AllAgentIdResources` existem em `v1.0`: NÃO VERIFICADO. Se uma política de workload identities com `ServicePrincipalsInMyTenant` apanha agent identities: NÃO VERIFICADO. | Qualquer automação de contenção tenant-wide por política depende de API beta. Ver ADR 0009. | M05, M06, M10 |
| L-16 | CONTRADITÓRIO | O agent user "available only to tenants participating in the Frontier preview program" no contexto Agent 365. As páginas Entra e a API `agentUser` em `v1.0` não mencionam preview. | Provável (inferência): o objecto existe em `v1.0`, e as capacidades Microsoft 365 dependem do Frontier. O MVP usa só o modo aplicação. | M14, M21 |
| L-17 | NÃO VERIFICADO | Retenção e latência da telemetria Agent 365, valor numérico do rate limit, API de leitura (`Agent365.Observability.OtelRead` não tem API documentada nas páginas lidas), cobertura de agentes sem instrumentação, disponibilidade em clouds soberanas. Um `200 OK` não prova ingestão (spans rejeitados por falta de licença). | A observabilidade Agent 365 não pode ser a evidência primária. Fica complementar à ledger. | M15, M16 |
| L-18 | NÃO VERIFICADO | Papel administrativo do Entra necessário para dar o consentimento de administrador às permissões de aplicação. Não foi aberta a documentação de consentimento. | Campo da matriz de permissões por preencher. | M22 |

### 2.2 SharePoint e Microsoft Graph

| Id | Estado | O que a documentação diz ou omite | Impacto no projecto | Fonte |
|---|---|---|---|---|
| L-19 | CONTRADITÓRIO | A visão geral de Selected permissions diz que os scopes Selected dão acesso aos recursos por qualquer caminho Graph, com papel `read` para ler metadados e conteúdo. Nenhuma das quatro páginas de leitura (listar bibliotecas, listar filhos, obter metadados, descarregar) lista um scope `*.Selected` na tabela de permissões de aplicação: a menor é `Files.Read.All`, de âmbito de tenant inteiro. | É a lacuna mais importante para o MVP. O desenho assume `Sites.Selected` com papel `read` (inferência da visão geral). Tem de ser confirmado endpoint a endpoint no tenant antes de qualquer afirmação. Nunca usar `Files.Read.All` para contornar. Ver R-07. | M22, M25, M26, M27 |
| L-20 | CONTRADITÓRIO | Quem pode atribuir permissões a lista e item: a visão geral exige `Sites.FullControl.All` ou `Sites.Selected` com papel FullControl ou Owner. A referência da API indica `Sites.ReadWrite.All` como menor privilégio e não lista scopes Selected. | O desenho assume o requisito mais forte (visão geral) até teste. A identidade que atribui permissões é separada do executor. | M22, M24 |
| L-21 | CONTRADITÓRIO | Criar permissão no site aceita modo delegado, remover permissão no site não ("Not supported."). | Revogar ao nível do site exige identidade de aplicação com `Sites.FullControl.All`, ou revogar o consentimento no Entra. Afecta o desenho do adaptador de remoção de grants. | M23, M66 |
| L-22 | NÃO VERIFICADO | Estado GA ou preview de cada nível Selected: as palavras "GA" e "preview" não constam. A API de atribuição está em `v1.0` para site, lista e item sem aviso beta. A página v1.0 de atribuição em `driveItem` não foi lida por inteiro, e as páginas de remoção em lista e item só apareceram em `beta`. | Ver ADR 0009. Pode forçar uso de `beta` para revogação ao nível de lista ou item. | M22, M24, M67 |
| L-23 | NÃO VERIFICADO | Listar e obter permissões de um site (`GET .../permissions`). A página de remoção em item escreve um caminho sem o segmento `/permissions/`, provável erro de página (inferência). | A reconciliação precisa de listar o que está atribuído. Sem esta leitura confirmada, T-11 e T-12 não ficam fechados. | M22 |
| L-24 | NÃO VERIFICADO | Se `/search/query` respeita `Sites.Selected`. A única fonte é uma resposta de fórum (Microsoft Q&A) que diz que não. Não é documentação de produto. | O MVP não usa pesquisa. Qualquer operação de pesquisa futura fica bloqueada até prova. | pesquisa |
| L-25 | CONTRADITÓRIO | Validade do URL pré-autenticado de descarga: "might expire within minutes" numa página, "(1 hour)" noutra, e "removing file permissions for a user might not immediately invalidate the URL". Anfitriões de destino do 302 em SharePoint Online: NÃO VERIFICADO (o exemplo é de OneDrive pessoal). | Pior caso de 1 hora. A lista de anfitriões permitidos no executor fica por definir no tenant. Ver R-08. | M25, M28 |
| L-26 | NÃO VERIFICADO | Limite de tamanho do corpo de `$batch`. A página remete para "known issues" fora do Learn, que não foi lido. | A política do gateway nega `$batch` por omissão e este limite deixa de ser necessário no MVP. | M29 |
| L-27 | CONTRADITÓRIO | Cabeçalhos `RateLimit` no SharePoint: recomendados na lista de boas práticas e declarados "not return or support" na secção própria da mesma página. | O projecto depende só de `Retry-After`. | M31 |

### 2.3 Exchange (fase posterior, fora do MVP)

| Id | Estado | O que a documentação diz ou omite | Impacto no projecto | Fonte |
|---|---|---|---|---|
| L-28 | NÃO VERIFICADO | Estado GA do RBAC for Applications. A página não diz "preview" nem "GA". O endereço de contacto chama-se `exoapprbacpreview@microsoft.com` e a vista consolidada no Entra "is upcoming". | Registar como risco de preview até confirmação. Ver ADR 0009. | M32 |
| L-29 | CONTRADITÓRIO | O RBAC for Applications "replaces" as Application Access Policies, mas só estas restringem permissões dadas no Entra. Sem data de descontinuação anunciada para as políticas legadas. | Quem tem permissões amplas no Entra e quer limitá-las tem de as remover. Não usar a funcionalidade legada em implementação nova. | M32, M33 |
| L-30 | NÃO VERIFICADO | Não existe papel de aplicação só de rascunhos. O mais próximo é `Application Mail.ReadWrite`, que permite ler, alterar e apagar correio no âmbito, sem envio. A diferença prática entre `Mail.ReadWrite` e `Mail-Advanced.ReadWrite.All` não é explicada. | A restrição a rascunhos faz-se no executor, não no Exchange. A permissão externa é mais ampla que a capacidade. | M32 |
| L-31 | CONTRADITÓRIO | A atribuição de permissões RBAC soma-se às do Entra ("union"). A propagação de alterações leva entre 30 minutos e 2 horas, e o cmdlet de teste ignora a cache e exclui permissões do Entra. | Um teste positivo ou negativo do cmdlet não prova o estado efectivo. A revogação de correio não é imediata. | M32 |

### 2.4 APIM, ledger, digests e infraestrutura

| Id | Estado | O que a documentação diz ou omite | Impacto no projecto | Fonte |
|---|---|---|---|---|
| L-32 | NÃO VERIFICADO | Não encontrada documentação que permita restringir o Microsoft Graph a aceitar pedidos só de um APIM. Os mecanismos de rede (rede virtual, private endpoint, lista de IPs, mTLS) aplicam-se ao troço APIM para backend. | O contorno do gateway por quem tem credencial Graph não é impedido por rede. O controlo real é o agente não possuir credencial. Ver R-06 e ADR 0003. | M55, M38 |
| L-33 | NÃO VERIFICADO | O "AI gateway" do APIM governa tráfego de modelos e ferramentas MCP e A2A. Não tem capacidade documentada para semântica de Graph, SharePoint ou Exchange, nem para `$batch` ou redireccionamentos 302. A disponibilidade das capacidades varia por escalão do APIM. | A autorização fina de operações fica por construir. O APIM não a transforma automaticamente. | M34 |
| L-34 | NÃO VERIFICADO | A política `validate-azure-ad-token` só obriga a `tenant-id`. O comportamento de cache de chaves de assinatura só está documentado para `validate-jwt`. A predefinição de `authentication-endpoint` aparece com hífen (`login-microsoftonline.com`), provável erro tipográfico. A lista completa de atributos de `validate-jwt` não foi lida por inteiro. | O modelo de política tem de impor `audiences` e aplicação cliente. O comportamento sob falha do Entra para `validate-azure-ad-token` fica por validar. | M35, M36 |
| L-35 | NÃO VERIFICADO | Frase literal "generally available" para o SQL Ledger em Azure SQL Database. As páginas mostram "Applies to" sem marca de preview. Número e texto do erro em `UPDATE` ou `DELETE` numa tabela append-only: só numa imagem. Colunas de `sys.database_ledger_blocks`. Limite de tamanho de transacção (só o limite de 200 tabelas por transacção). | Ver ADR 0004 e 0009. Testes de BT-16 não podem assertar o texto do erro. | M42, M43, M44, M63 |
| L-36 | NÃO VERIFICADO | Digests: se uma identidade user-assigned do servidor SQL pode fazer o upload (a documentação fala em "System Identity"); comportamento com rede privada ou firewall na conta de armazenamento; se o contentor `sqldbledgerdigests` pode ser pré-criado com a política antes do primeiro upload; se o `lock` da política pode ser declarado em Bicep; nomes das métricas de upload. | O Bicep da fase C pode não conseguir declarar tudo. O lock da política é tratado como passo manual e irreversível. | M41, M45, M46, M58 |
| L-37 | CONTRADITÓRIO | Com autenticação Entra-only, Elastic Jobs não é suportado, e é a opção que a documentação de ledger cita para agendar a verificação (a par de Azure Automation). | O agendamento da verificação usa Azure Automation ou um job próprio (escolha do projecto, não consta da documentação). | M40, M53 |
| L-38 | NÃO VERIFICADO | Azure Confidential Ledger: tipo e `apiVersion` Bicep, natureza do custo, e a tabela comparativa com Blob imutável que a página de digests refere. Limites verificados: 2 ledgers por subscrição, 8 KiB por entrada, replicação para o par regional sem poder desactivar. | O Confidential Ledger fica como opção, não como baseline. Ver ADR 0004. | M41, M48, M49 |
| L-39 | NÃO VERIFICADO | Valores de `sku` de SQL Database e de Service Bus na referência Bicep, valores de `workloadProfileType`, formato do bloco `identity` de Container Apps, `accessPolicies` quando `enableRbacAuthorization` é true. Aceitação real das `apiVersion` de 2026 pelo Bicep CLI e pelas regiões. | O Bicep está escrito e nunca foi compilado contra uma subscrição. Tratar como não validado. | pesquisa |
| L-40 | NÃO VERIFICADO | Service Bus: disponibilidade de sessões por escalão, se `requiresSession` pode mudar depois de criar a fila. A detecção de duplicados cobre o envio, não a reentrega ao consumidor. | A ordem de mensagens não pode ser assumida. O desenho usa epoch e idempotência. Ver ADR 0005. | M56, M51, M50 |
| L-41 | NÃO VERIFICADO | Sintaxe exacta de `GRANT` para `ENABLE LEDGER`, `VIEW LEDGER CONTENT` e `GENERATE LEDGER DIGEST`. Natureza do custo da funcionalidade ledger, de Key Vault por operação, de API Management por escalão e de managed identities. | Estimativa de custos (`cost-estimate.md`) só pode descrever a natureza do custo e apontar a página oficial de preços. Sem valores. | pesquisa |
| L-42 | NÃO VERIFICADO | Datas de última actualização de todas as páginas de Graph, Exchange e APIM. Páginas lidas só por excerto: lifecycle policies de Agent ID, licenciamento Entra, ID Governance para agentes, bloqueio em Agent 365, e três exemplos de managed identities noutros serviços. | Antes de publicar qualquer destes factos, ler a página por inteiro. | M61, M62, M64 |

## 3. Pressupostos do desenho

Cada pressuposto diz o que o desenho assume, onde está no código ou nos documentos, e como se valida. Nenhum foi validado: não existe tenant.

| Id | Pressuposto | Onde se manifesta | Como se valida | Se falhar |
|---|---|---|---|---|
| P-01 | O identificador de site tem a forma `anfitrião,GUID,GUID`, com anfitrião em minúsculas e GUIDs em minúsculas. Os identificadores de drive e de item são texto opaco com `A-Za-z0-9!_-`, até 256 caracteres. A forma canónica de recurso é `sharepoint:site/{siteId}/drive/{driveId}`. | `ResourceCanonicalizer` e `OperationCatalog` em `src/Core/Domain/Operations.cs`. As expressões regulares são fechadas. | POR VALIDAR NO TENANT: obter identificadores reais do site e da biblioteca piloto e correr o resolvedor. | O resolvedor recusa identificadores reais (falha fechada, bloqueia o uso). Ajustar a expressão e repetir TC-07. |
| P-02 | O token do agente em modo aplicação transporta `tid` (tenant) e `oid` (identificador de objecto do service principal da agent identity), e `oid` é igual ao `Identity.ObjectId` do agente registado. | `CallerIdentity(TenantId, ObjectId)` e `FindAgentByIdentityAsync`. | POR VALIDAR NO TENANT: obter um token real de uma agent identity de teste, descodificar e comparar. A documentação lida não lista os claims destes tokens. | O mapeamento de identidade falha, o agente não é resolvido (`agent_unknown`). Ajustar o claim usado e repetir BT-02. |
| P-03 | O agente usa o modo aplicação (a agent identity é o sujeito do token). Os modos em nome do utilizador e agent user não fazem parte do MVP. | Âmbito inicial da especificação, secção 2. | Decisão de âmbito na fase A. | Sem a decisão, a revogação precisa cobrir três sujeitos de token (L-05). |
| P-04 | Um token válido do tenant não chega para passar no APIM: o modelo de política impõe `audiences` e aplicação cliente (ou `required-claims`). | Modelo de política APIM da fase C. | Fase C: token com audiência errada e com aplicação errada recebe 401 (TC-02). | Qualquer token do tenant passa no gateway. |
| P-05 | `Sites.Selected` com papel `read` basta para as quatro operações de leitura (listar bibliotecas, listar filhos, obter metadados, descarregar), apesar de as tabelas de referência listarem `Files.Read.All`. | Matriz de permissões e `IDocumentGateway`. | Fase C, endpoint a endpoint, com teste negativo noutro recurso. | O MVP deixa de ser viável com âmbito estreito. Parar e decidir. Nunca usar `Files.Read.All`. Ver R-07. |
| P-06 | A atribuição de permissões nas listas e itens exige `Sites.FullControl.All` (requisito mais forte), e esta identidade é diferente do executor. | Plano do adaptador de concessão (fase C). | Fase C: tentar com `Sites.ReadWrite.All` e confirmar o resultado (decide L-20). | Se bastasse menos, o requisito é aliviado. Se não bastasse nada do previsto, muda o desenho de concessão. |
| P-07 | `accountEnabled=false` numa agent identity impede a emissão de tokens novos para essa identidade, e o corpo do pedido é `{"accountEnabled": false}` sobre o endpoint `PATCH /servicePrincipals/{id}/microsoft.graph.agentIdentity`. | Adaptador de desactivação Entra (hoje simulado). | Fase D, com agent identity de teste. | A contenção externa não bloqueia emissão. O estado global não passa a `VerifiedScoped`. |
| P-08 | Um token emitido antes da desactivação pode ser guardado e reapresentado num recurso de teste, e o seu resultado pode ser medido. | BT-14 e a sonda de token antigo (hoje simulada). | Fase D, por tipo de identidade e por recurso. | Sem esta medição não existe afirmação possível sobre tokens já emitidos. |
| P-09 | O executor consegue seguir o 302 de `/content` e descarregar sem cabeçalho `Authorization`, para um conjunto finito de anfitriões que a lista de saída consegue enumerar. | `IDocumentGateway` (hoje simulado). | POR VALIDAR NO TENANT, fase C: observar os anfitriões reais do SharePoint Online. | A lista de anfitriões tem de ser alargada, ou o descarregamento falha fechado. |
| P-10 | Cada agente tem uma identidade de execução própria (`ExecutorObjectId`) com grant Selected no recurso, e as managed identities não são partilhadas entre runtimes com âmbitos incompatíveis. | Modelo `Agent` e regras de desenho 9 e 10. | Fase C, com a matriz de cobertura. | Cobertura baixa para `DirectAccessPossible`. |
| P-11 | A ledger é tabela a tabela (`WITH (LEDGER = ON (APPEND_ONLY = ON))` em `ap.AuditEvent`), e a base de dados não é ledger (`isLedgerOn` desligado), porque as restantes tabelas são actualizáveis. | `Storage.SqlServer` e Bicep (por aplicar). A confirmar no Bicep. | Decisão humana na fase A. A propriedade é irreversível depois de a base ser criada. | Se a base fosse ledger, todas as tabelas, inclusive as operacionais, passariam a ledger. Ver ADR 0004. |
| P-12 | O SQL Server 2022 local, em docker, comporta-se como o Azure SQL Database para a ledger append-only e para a verificação (excepto digest em armazenamento independente). | BT-16 local, executado contra SQL Server 2022 em contentor (`BT16_`, 2026-11-11). | Fase C: repetir BT-16 contra Azure SQL com digests em Blob. | Diferenças aparecem só na fase C. |
| P-13 | O destino dos digests é Blob imutável com política `locked` e redundância que não seja LRS, e a retenção é decidida antes do lock. | Bicep (por aplicar). | Decisão humana de retenção e fase C. | Depois do lock a retenção só pode aumentar. |
| P-14 | A outbox na base de dados é a fonte de entrega. O transporte alvo para a fase C é o Service Bus, e o `ContainmentWorker` local lê a outbox directamente. | Regra de desenho 4 e ADR 0005. | Fase C, BT-09 e BT-10 contra Service Bus real. | O comportamento de entrega real (reentrega, dead-letter, janela de duplicados) não foi exercido. |
| P-15 | O intervalo da reconciliação periódica será definido pelo dono, e a reconciliação tem permissões de leitura no Entra e no recurso. | `ReconciliationService`. | Decisão na fase A, validação na fase C. | Drift fica por detectar durante o intervalo. |
| P-16 | O tempo de 5 segundos para recusar novas admissões após suspensão persistida é um objectivo inicial, medido no ambiente piloto, não uma promessa. | SLO da secção 15 da especificação. | Fase C. | Se o objectivo falhar, descreve-se o valor medido. |
| P-17 | Os identificadores de objecto de operadores e responsáveis guardados em eventos podem ser dados pessoais. A pseudonimização, a retenção e a eliminação são decididas antes de bloquear a imutabilidade. | Eventos de ledger e política de retenção. | Decisão humana, com o responsável de protecção de dados. Não se declara conformidade RGPD por usar serviços Microsoft. Ver R-05. | Dados pessoais ficam imutáveis. |
| P-18 | Há um único tenant piloto, um runtime Azure controlado pela equipa, um agente de leitura de SharePoint e uma biblioteca autorizada, com teste negativo noutro recurso. | Âmbito inicial da especificação, secção 2. | Aprovação do âmbito na fase A. | Sem âmbito aprovado o gate não passa. |
| P-19 | A segregação de papéis (Reader, IncidentOperator, PolicyAdmin, Auditor) mapeia para app roles do Entra com os mesmos nomes. | `Roles` em `src/Core/Domain/Vocabulary.cs`. | Fase C, com o tenant. | Os papéis não chegam ao token, e a ControlApi nega tudo. |
| P-20 | O break-glass é independente da aplicação, documentado e ensaiado. | BT-20, runbooks. | Fase D, ensaio real. | Sem acesso de emergência com a aplicação em baixo. |
| P-21 | O limite por omissão de timeout externo (10 segundos) e o limite por hora por agente são valores de arranque do código, não de calibração. | `ExecutionOptions.ExternalTimeout` (configurável por `Execution__ExternalTimeoutSeconds`) e o campo `maxActionsPerHour` do agente. | Medir na fase C. | Falsos `Unknown` por timeout curto, ou limite inadequado. |

## 4. Marcos bloqueados por fase

O que não avança sem tenant ou sem decisão humana. A especificação (instrução 7) manda bloquear o marco correspondente, não esconder a lacuna com mocks.

### 4.1 Fase A: validação

Estado: documentos escritos. Gate por aprovar por humano: aprovação de âmbito, licenças e permissões.

| Marco | Bloqueado por | Desbloqueio |
|---|---|---|
| Campos do tenant em `environment-assessment.md` (licenças, runtimes, permissões, retenção, residência) | Não há tenant. Ficam "POR PREENCHER NO TENANT". | Acesso ao tenant piloto. |
| Matriz de permissões completa (papel administrativo, licença por operação) | Licença por operação NÃO VERIFICADA (L-14). Papel de consentimento NÃO VERIFICADO (L-18). | Teste em tenant, e confirmação das licenças pelo dono. |
| Aceitação das dependências em preview | Decisão humana (ADR 0009). | Aprovação formal. |
| Retenção, pseudonimização e eliminação dos dados na ledger | Decisão humana, com protecção de dados (P-17). | Decisão antes do lock da imutabilidade. |
| Atribuição de owners das ameaças e dos riscos | Decisão do dono do projecto. | Nomear os papéis. |

### 4.2 Fase B: fatia local

Estado: implementada. Sem credenciais reais. Gate: invariantes e concorrência testados, por aprovar por humano.

| Marco | Bloqueado por | Desbloqueio |
|---|---|---|
| BT-05, BT-17, BT-20 | Precisam de recursos reais. A aproximação local não conta como prova. BT-20 não tem teste. | Fases C e D. |
| BT-06, BT-13, BT-14 | Só existem em simulação. Não contam como prova. | Fases C e D. |
| Testes TC locais (TC-01, 04 a 10, 14, 18) | Não bloqueados. TC-08, TC-09, TC-14 e TC-18 existem. TC-01, TC-04, TC-05, TC-06 e TC-07 existem em parte. TC-10 por implementar. | Escrever e correr localmente. Ver `test-plan.md`. |

### 4.3 Fase C: Azure de teste

Estado: não executada. Bicep escrito, nunca aplicado.

| Marco | Bloqueado por | Desbloqueio |
|---|---|---|
| Qualquer recurso Azure | Sem subscrição, sem tenant, sem aprovação de custos e escalões. | Subscrição de teste e gate A aprovado. |
| Matriz de permissões: validação endpoint a endpoint | L-19, L-20, L-21 (contradições de Selected). | Teste em tenant com recurso autorizado e recurso negativo. |
| Claims `tid` e `oid` do agente | P-02. | Token real de uma agent identity de teste. |
| Formato de identificadores de site, drive e item | P-01. | Identificadores reais. |
| Anfitriões do 302 em SharePoint Online | L-25, P-09. | Observação no tenant. |
| Política APIM (`validate-azure-ad-token`, negar `$batch`) | Escalão do APIM e disponibilidade das capacidades (L-33). | Decisão de escalão e compilação do Bicep. |
| SQL Ledger e digests com política `locked` | Decisão de `isLedgerOn` (P-11), retenção (P-13), lock manual (L-36). | Decisões humanas e subscrição. |
| Service Bus (BT-09, BT-10 reais) | Subscrição e decisão de escalão. Sessões por escalão NÃO VERIFICADO (L-40). | Subscrição. |
| BT-05 e BT-06: prova de impossibilidade de acesso directo | Todos os acima. | Gate da fase C. |
| BT-17: restauro e failover | Azure SQL real com geo-replicação. | Fase C. |

### 4.4 Fase D: contenção

Estado: não executada. Nenhum adaptador real para Entra nem para Graph.

| Marco | Bloqueado por | Desbloqueio |
|---|---|---|
| Adaptador real de desactivação Entra | Corpo do pedido NÃO VERIFICADO (L-02), `accountEnabled` ambíguo (L-03). | Agent identity de teste e papel Agent ID Administrator. |
| Remoção real de grants Selected | Atribuição e remoção assimétricas (L-21), v1.0 em lista e item NÃO VERIFICADO (L-22). | Teste no tenant. |
| BT-14: token previamente emitido | L-04, L-11, L-12, L-13. | Medição por tipo de identidade e por recurso. |
| BT-13 real: irmãos do blueprint | L-05, L-08. | Tenant com blueprint e duas agent identities. |
| Managed identities: isolamento sem disable | L-13. | Tenant, runtime e managed identity de teste. |
| Relação entre bloqueio em Agent 365 e Entra | L-06. | Teste no tenant, se Agent 365 estiver licenciado. |
| Agent user | L-16 e programa Frontier. | Fora do MVP. |
| BT-20: break-glass | Runbook independente (P-20). | Ensaio real. |
| Correio com âmbito (rascunhos) | L-28, L-30, L-31. Fora do MVP. | Decisão de entrar na fase posterior. |

### 4.5 Fase E: piloto

Estado: não executada. Gate: aprovação humana de produção, zero riscos críticos não mitigados ou aceites formalmente.

| Marco | Bloqueado por | Desbloqueio |
|---|---|---|
| Teste de carga | Testes de carga não são permitidos em SharePoint Online [M31]. | Plano que dispense carga sobre SharePoint real. |
| Revisão de segurança, recuperação, retenção, custos, operação | Fases C e D concluídas. | Gates anteriores. |
| Aprovação de produção | Todos os riscos em `risks.md` decididos (aceitar, mitigar ou bloquear). | Decisão humana. |

## 5. Fontes

Todas verificadas em 2026-11-11. Prefixo: `https://learn.microsoft.com/en-us/`. "Leitura" indica se a página foi lida por inteiro ou só por excerto de pesquisa.

| Chave | Caminho | Leitura |
|---|---|---|
| M01 | `entra/identity/users/users-revoke-access` | inteira |
| M02 | `entra/identity/conditional-access/concept-continuous-access-evaluation` | inteira |
| M03 | `entra/identity/conditional-access/concept-continuous-access-evaluation-workload` | inteira |
| M04 | `entra/identity/conditional-access/agent-id` | inteira |
| M05 | `entra/agent-id/disable-agent-identities` | inteira |
| M06 | `entra/agent-id/manage-agent-identities-admin` | inteira |
| M07 | `entra/identity-platform/configurable-token-lifetimes` | inteira |
| M08 | `entra/identity-platform/access-tokens` | inteira |
| M09 | `entra/identity/managed-identities-azure-resources/managed-identities-faq` | inteira |
| M10 | `entra/identity/conditional-access/workload-identity` | inteira |
| M11 | `entra/identity/enterprise-apps/disable-user-sign-in-portal` | inteira |
| M12 | `entra/identity/enterprise-apps/deactivate-app-registration` | inteira |
| M13 | `entra/agent-id/key-concepts` | inteira |
| M14 | `microsoft-agent-365/developer/identity` | inteira |
| M15 | `microsoft-agent-365/developer/observability-concepts` | inteira |
| M16 | `microsoft-agent-365/overview` | inteira |
| M17 | `graph/api/agentidentity-update?view=graph-rest-1.0` | inteira |
| M18 | `graph/api/resources/agentidentity?view=graph-rest-1.0` | inteira |
| M20 | `entra/agent-id/manage-agent-blueprint` | inteira |
| M21 | `entra/agent-id/agent-users` | inteira |
| M22 | `graph/permissions-selected-overview` | inteira |
| M23 | `graph/api/site-post-permissions?view=graph-rest-1.0` | inteira |
| M24 | `graph/api/list-post-permissions?view=graph-rest-1.0` | inteira |
| M25 | `graph/api/driveitem-get-content?view=graph-rest-1.0` | inteira |
| M26 | `graph/api/driveitem-get?view=graph-rest-1.0` | inteira |
| M27 | `graph/api/driveitem-list-children?view=graph-rest-1.0` | inteira |
| M28 | `graph/api/resources/driveitem?view=graph-rest-1.0#instance-attributes` | excerto |
| M29 | `graph/json-batching` | inteira |
| M31 | `sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online` | inteira |
| M32 | `exchange/permissions-exo/application-rbac` | inteira |
| M33 | `exchange/permissions-exo/application-access-policies` | inteira |
| M34 | `azure/api-management/genai-gateway-capabilities` | inteira |
| M35 | `azure/api-management/validate-azure-ad-token-policy` | inteira |
| M36 | `azure/api-management/validate-jwt-policy` | excerto |
| M38 | `entra/msidweb/advanced/api-gateways#follow-best-practices` | excerto |
| M40 | `sql/relational-databases/security/ledger/ledger-database-verification` | inteira |
| M41 | `sql/relational-databases/security/ledger/ledger-digest-management` | inteira |
| M42 | `sql/relational-databases/security/ledger/ledger-limits` | inteira |
| M43 | `sql/relational-databases/security/ledger/ledger-database-ledger` | inteira |
| M44 | `sql/relational-databases/security/ledger/ledger-append-only-ledger-tables` | inteira |
| M45 | `sql/relational-databases/security/ledger/ledger-monitor-digest-uploads` | inteira |
| M46 | `azure/storage/blobs/immutable-storage-overview` | inteira |
| M48 | `azure/confidential-ledger/overview` | inteira |
| M49 | `azure/confidential-ledger/limits` | inteira |
| M50 | `azure/service-bus-messaging/service-bus-message-loss-and-duplicates` | excerto |
| M51 | `azure/service-bus-messaging/duplicate-detection` | inteira |
| M53 | `azure/azure-sql/database/authentication-azure-ad-only-authentication` | inteira |
| M55 | `azure/api-management/secure-api-management#network-security` | excerto |
| M56 | `azure/service-bus-messaging/message-sessions` | excerto |
| M58 | `sql/relational-databases/security/ledger/ledger-how-to-enable-automatic-digest-storage` | inteira |
| M60 | `entra/agent-id/what-is-microsoft-entra-agent-id` | inteira |
| M61 | `entra/id-governance/agent-lifecycle-policies` | excerto |
| M62 | `entra/fundamentals/licensing` | excerto |
| M63 | `azure/azure-sql/database/ledger-landing` | inteira |
| M64 | `microsoft-agent-365/admin/manage-agents` | excerto |
| M66 | `graph/api/site-delete-permission?view=graph-rest-1.0` | excerto |
| M67 | `graph/api/driveitem-post-permissions?view=graph-rest-beta` | inteira |
