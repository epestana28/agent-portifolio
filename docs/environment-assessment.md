# Avaliação do ambiente (environment assessment)

Estado: rascunho por validar no tenant

Verificado em documentação oficial a 2026-11-11

## 1. Âmbito e estado real

Este documento cumpre a secção 4 da especificação. Não existe tenant, por isso é uma avaliação em duas partes por tema.

A parte "O que a documentação oficial estabelece" contém só o que foi lido em Microsoft Learn, com URL. A parte "Por preencher no tenant" lista os campos concretos que só o tenant responde e a forma geral de os obter. Nenhum valor de tenant foi inventado.

Estado do projecto, sem exagero:

- Fase A (documentos de validação): escrita a partir de documentação oficial, sem tenant.
- Fase B (fatia vertical local em .NET 8): implementada sem credenciais reais. O mundo Microsoft é simulado e está assinalado como simulação.
- Fases C, D e E: não executadas. O Bicep está escrito e nunca foi aplicado. Não existe adaptador real para Entra nem para Graph.

Legenda usada em todo o documento:

- Verificado: texto lido na página citada.
- Inferência: conclusão tirada de dois ou mais textos. A documentação não a afirma.
- Por validar no tenant: só um teste real responde.
- NÃO VERIFICADO: a documentação lida não o afirma. Não significa que seja falso.

Todas as fontes foram lidas em Microsoft Learn e verificadas a 2026-11-11. Prefixo dos URL: `https://learn.microsoft.com/en-us/`. Quando uma página só foi lida por excerto de pesquisa, isso fica indicado e a afirmação deve ser reconfirmada antes de ser usada como facto.

## 2. Tenant, subscrição, região e classificação de dados

### O que a documentação oficial estabelece

- Agent identities são single-tenant e só recebem tokens no tenant onde foram criadas. Blueprints podem ser multitenant. Fonte: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity`.
- O Agent 365 é GA para o segmento Commercial, por utilizador, desde 2026-05-01. Fonte: `https://learn.microsoft.com/en-us/microsoft-agent-365/overview`. Disponibilidade em GCC, GCC High e DoD: NÃO VERIFICADO.
- CAE está disponível em tenants Azure Government (GCC High e DoD) para Exchange Online. Fonte: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation`.
- O Azure Confidential Ledger replica os dados para o par regional e não permite alterar nem desactivar essa replicação. Fonte: `https://learn.microsoft.com/en-us/azure/confidential-ledger/secure-confidential-ledger`.
- A gestão automática de digests do SQL Ledger com Blob imutável não aceita contas LRS. Fonte: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits`.
- A documentação lida não define classificação de dados nem fala de RGPD. A especificação (secção 11) proíbe declarar conformidade RGPD automática por usar serviços Microsoft.
- Inferência: as tabelas ledger não permitem apagar linhas e o contentor com política locked não permite apagar blobs antes do fim da retenção. Por isso, dados pessoais não devem entrar nelas.

### Por preencher no tenant

| Campo | Forma geral de obter | Valor |
| --- | --- | --- |
| ID do tenant | Propriedades do tenant no centro de administração Entra | POR PREENCHER NO TENANT |
| Tipo de tenant (piloto, produção, partilhado) | Confirmação do responsável pelo tenant | POR PREENCHER NO TENANT |
| Segmento de cloud (Commercial ou soberano) | Confirmação do responsável pelo tenant | POR PREENCHER NO TENANT |
| Subscrição Azure do piloto | Lista de subscrições no portal Azure | POR PREENCHER NO TENANT |
| Região primária e região par | Decisão de arquitectura, validada contra as regiões que oferecem cada serviço | POR PREENCHER NO TENANT |
| Participação no programa Frontier preview | Confirmação no centro de administração Microsoft 365 ou com o responsável pelo tenant | POR PREENCHER NO TENANT |
| Classificação de dados do site e da biblioteca piloto | Política interna de classificação, aplicada ao site escolhido | POR PREENCHER NO TENANT |
| Existência de dados pessoais nos documentos do piloto | Revisão do conteúdo com o responsável do site | POR PREENCHER NO TENANT |

## 3. Licenças e custos incrementais

### O que a documentação oficial estabelece

| Capacidade | Licença declarada | Fonte |
| --- | --- | --- |
| Plataforma Entra Agent ID (criar e gerir identidades e blueprints) | "Agent ID is available for all Microsoft Entra customers." | `https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id` |
| Segurança Entra estendida a agentes | Exige Microsoft Agent 365. Incluído no Microsoft 365 E7. Complemento para E5, A5 e Business Premium | mesma página |
| Conditional Access para agentes (página de 2026-07-31) | Microsoft 365 E7, ou licença Agent 365 com pelo menos Entra P1 ou Microsoft 365 E3 | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id` |
| Conditional Access para agentes (página de administração) | "Applying Conditional Access policies in your tenant requires the Microsoft Entra ID P1 license." | `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities` |
| ID Protection para agentes | Entra ID P2 durante a preview. Excerto de outra página indica que passará a exigir licença Agent 365, sem data | `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin` e `https://learn.microsoft.com/en-us/entra/fundamentals/licensing` (excerto) |
| ID Governance para agentes | E7, ou Agent 365 com pelo menos Entra P1 ou Microsoft 365 E3 (excerto) | `https://learn.microsoft.com/en-us/entra/id-governance/agent-id-governance-overview` |
| Conditional Access para service principals | Workload Identities Premium para criar ou alterar políticas. Políticas existentes continuam a funcionar sem licença, mas não se alteram | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity` |
| Agent 365 | Por utilizador. Pelo menos um utilizador tem de ter licença qualificante atribuída. Funciona melhor com Microsoft E5 como pré-requisito | `https://learn.microsoft.com/en-us/microsoft-agent-365/overview` |
| Telemetria Agent 365 | Sem licença atribuída, o pedido devolve `200 OK` e cada span fica `rejected` com `tenant_not_licensed`. Ter o SKU presente no tenant não basta | `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability-concepts` |

Contradição de licenças (assinalada na pesquisa como C1). Duas páginas oficiais dão requisitos diferentes para Conditional Access aplicado a agentes: Entra ID P1 (página de administração) contra Microsoft 365 E7 ou Agent 365 com P1 ou E3 (página de Conditional Access para agentes, a mais recente). Tratamento neste projecto: documentar Agent 365 como requisito provável, citar a divergência e decidir no tenant.

Segunda divergência (C2). ID Protection para agentes exige P2 "during preview", mas outra página diz que "Starting soon" exigirá licença Agent 365. O requisito está em transição, sem data.

Preços do Agent 365: NÃO VERIFICADO. As páginas remetem para `microsoft.com/microsoft-agent-365`, fora de Microsoft Learn. Não se escrevem valores.

Requisito de licença por operação Graph (SharePoint Selected, desactivação de identidades, revogação de sessões): nenhuma página lida o indica. A ausência de menção não prova que não exista requisito. Ver `permission-matrix.md`.

### Por preencher no tenant

| Campo | Forma geral de obter | Valor |
| --- | --- | --- |
| SKUs presentes no tenant | Página de licenças do centro de administração Microsoft 365 | POR PREENCHER NO TENANT |
| SKUs com licença efectivamente atribuída (Agent 365, E7, E5, E3) | Atribuições por utilizador. Confirmar que pelo menos um utilizador tem a licença atribuída | POR PREENCHER NO TENANT |
| Entra ID P1 e P2 | Página de licenças do Entra | POR PREENCHER NO TENANT |
| Workload Identities Premium | Página de licenças do Entra | POR PREENCHER NO TENANT |
| Defender, Purview e Sentinel (complementos opcionais) | Licenças e subscrições existentes | POR PREENCHER NO TENANT |
| Custo incremental por licença | Contrato do tenant e páginas oficiais de preços. Ver `cost-estimate.md` | POR PREENCHER NO TENANT |
| Quem aprova o custo incremental | Responsável pelo orçamento do piloto | POR PREENCHER NO TENANT |

## 4. Funcionalidades disponíveis, GA ou preview, restrições de região e cloud

### O que a documentação oficial estabelece

A coluna "Estado" transcreve o que a página diz. Quando a página não usa as palavras GA ou preview, a linha diz "não declarado". Não se presume GA por a API estar em v1.0.

| Funcionalidade | Estado documentado | Fonte |
| --- | --- | --- |
| Entra Agent ID, produto | "Available for all Microsoft Entra customers". A palavra GA não consta. NÃO VERIFICADO como afirmação textual | `https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id` |
| APIs Graph de agentIdentity, blueprint, principal e agentUser | Publicadas em v1.0. Inferência: indica suporte de produção, sem declaração de GA | `https://learn.microsoft.com/en-us/graph/api/resources/agentidentity?view=graph-rest-1.0` |
| Políticas Conditional Access para agentes por Graph | Exemplos em `beta` | `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities` |
| Sign-in logs de agentes por Graph | Exemplo em `beta` | `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin` |
| Condição "Agent execution environments" | Preview | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id` |
| ID Protection para agentes | Preview | `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin` |
| Editor de manifesto do blueprint | Preview | `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-blueprint` |
| Lifecycle policies de Agent ID | Preview (excerto de pesquisa, página não lida por inteiro) | `https://learn.microsoft.com/en-us/entra/id-governance/agent-lifecycle-policies` |
| Agent 365 | GA desde 2026-05-01, segmento Commercial | `https://learn.microsoft.com/en-us/microsoft-agent-365/overview` |
| Conta de utilizador do agente e modo Agentic-User no Agent 365 | Só em tenants no programa Frontier preview | `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity` |
| Agent 365 Observability SDK | Deprecated. Recomendado o Microsoft OpenTelemetry Distro | `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability-concepts` |
| CAE para utilizadores | Documentada sem marca de preview | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation` |
| CAE para workload identities | Documentada sem marca de preview, com restrições (ver secção 6) | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation-workload` |
| CAE para agent identities | NÃO VERIFICADO. A única menção é "Some resources also support Continuous Access Evaluation" | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id` |
| Selected scopes (Sites, Lists, ListItems, Files) | GA ou preview não declarado por nível. Modos delegado e aplicação suportados | `https://learn.microsoft.com/en-us/graph/permissions-selected-overview` |
| Atribuição de permissão em site, lista e item | Presente em v1.0 sem aviso beta. Remoção em lista e item só encontrada em beta. Inferência: disponibilidade geral da API, sem declaração de GA do scope | páginas `site-post-permissions`, `list-post-permissions`, `listitem-post-permissions` de Microsoft Graph |
| Exchange RBAC for Applications | GA ou preview não declarado. Sinais opostos: contacto de preview na página e vista consolidada no Entra "upcoming" | `https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac` |
| Application Access Policies (Exchange) | Legacy. "New access configuration should not use Application Access Policies". Sem data de descontinuação | `https://learn.microsoft.com/en-us/exchange/permissions-exo/application-access-policies` |
| APIM AI gateway | Parte do APIM. "Unified model API" e "AI gateway in Microsoft Foundry" em preview. Disponibilidade varia por tier | `https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities` |
| APIM `validate-azure-ad-token` | "Microsoft Entra ID for customers (preview) is not supported" | `https://learn.microsoft.com/en-us/azure/api-management/validate-azure-ad-token-policy` |
| SQL Ledger em Azure SQL Database | Sem marca de preview. Frase literal de GA: NÃO VERIFICADO | `https://learn.microsoft.com/en-us/azure/azure-sql/database/ledger-landing` |
| Azure Confidential Ledger | GA (excerto de pesquisa) | `https://learn.microsoft.com/en-us/azure/confidential-computing/overview-azure-products` |
| Blob imutável | Documentação estável. Incompatível com point-in-time restore e last access tracking. Sem suporte com NFS 3.0 ou SFTP | `https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview` |
| apiVersions Bicep de 2026 | Documentadas. Aceitação real pelo Bicep CLI e por cada região: NÃO VERIFICADO | referência de templates Azure em Microsoft Learn |

Regra da especificação (secção 0, ponto 8): funcionalidade preview não é dependência obrigatória de produção sem aceitação explícita do risco e alternativa documentada. Aplica-se a ID Protection para agentes, condição "Agent execution environments", lifecycle policies e agent user.

### Por preencher no tenant

| Campo | Forma geral de obter | Valor |
| --- | --- | --- |
| Cada funcionalidade da tabela existe e está visível no tenant | Verificação nos centros de administração Entra e Microsoft 365 | POR PREENCHER NO TENANT |
| Cada serviço Azure está disponível na região escolhida | Disponibilidade por região na documentação de cada serviço, confirmada no portal ao criar o recurso | POR PREENCHER NO TENANT |
| Estado GA ou preview dos Selected scopes por nível | Teste de atribuição e de leitura no tenant. A documentação não declara | POR PREENCHER NO TENANT |
| Estado do RBAC for Applications no tenant | Confirmação com o administrador Exchange e teste de atribuição | POR PREENCHER NO TENANT |
| Decisão sobre cada dependência preview (aceitar, adiar, alternativa) | Aprovação de risco, ver secção 10 | POR PREENCHER NO TENANT |

## 5. Runtimes e tipos de identidade existentes

### O que a documentação oficial estabelece

- Modelo de objectos do Agent ID: blueprint (herda de `application`), blueprint principal (herda de `servicePrincipal`), agent identity (service principal com `servicePrincipalType` igual a `ServiceIdentity`) e agent user (subtipo de utilizador, 1:1 com a agent identity). Fonte: `https://learn.microsoft.com/en-us/entra/agent-id/key-concepts`.
- Um blueprint pode ter várias agent identities. As credenciais ficam só no blueprint e são partilhadas por todas as identidades filhas: "Treat each blueprint as a credential boundary". Fonte: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity`.
- Tipos de credencial do blueprint: federated identity credentials, certificados e chaves, client secrets. Para agentes em Azure, o blueprint pode ser federado com uma managed identity para não guardar segredo. Mesma fonte.
- Três modos de execução: S2S (a agent identity é o sujeito do token), OBO (o utilizador é o sujeito e a agent identity é o actor) e Agentic-User. Mesma fonte.
- Agentes criados em versões anteriores do Copilot Studio e do Azure AI Foundry podem ser aplicações clássicas ou service principals e "don't have Microsoft Entra agent identities". Fonte: `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities`.
- Aplicações Entra clássicas, como as de custom engine agents, "aren't registered agent instances" no Agent 365. Criar a identidade Entra não regista a instância. Fonte: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability-concepts`.
- Managed identity só tem service principal, sem application. A fronteira de segurança é o recurso a que está associada. Fonte: `https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq`.
- Existe um segundo plano de controlo, o centro de administração Microsoft 365, com acção Block de agente e de instância (excertos de pesquisa). Se o Block altera `accountEnabled` no Entra: NÃO VERIFICADO.
- Inferência de desenho: o runtime piloto previsto é Azure Container Apps, com identidade de execução separada por agente. O runtime real do agente piloto ainda não foi escolhido neste projecto.

### Por preencher no tenant

| Campo | Forma geral de obter | Valor |
| --- | --- | --- |
| Runtime do agente piloto e quem o controla | Decisão da equipa. Confirmar que o runtime é controlado por ela (especificação, secção 2) | POR PREENCHER NO TENANT |
| Inventário de agent identities (objectId, appId, blueprint, estado, sponsor) | Lista de agent identities no centro de administração Entra | POR PREENCHER NO TENANT |
| Inventário de blueprints e blueprint principals, e se são multitenant | Lista de blueprints no centro de administração Entra | POR PREENCHER NO TENANT |
| Agent users existentes e identidade pai de cada um | Centro de administração Entra | POR PREENCHER NO TENANT |
| Service principals de aplicações com acesso a SharePoint ou a Graph | Lista de aplicações empresariais e permissões consentidas | POR PREENCHER NO TENANT |
| Managed identities (system e user-assigned), recursos a que estão associadas e se alguma é partilhada entre runtimes | Lista de managed identities no portal Azure e atribuições nos recursos | POR PREENCHER NO TENANT |
| Agentes Copilot Studio, Foundry, MCP e de terceiros sem agent identity | Inventário no centro de administração Microsoft 365 e nos serviços de origem | POR PREENCHER NO TENANT |
| Instâncias registadas no Agent 365 | Inventário de agentes no centro de administração Microsoft 365 | POR PREENCHER NO TENANT |

## 6. Conditional Access por tipo de identidade

Aviso. Conditional Access para utilizadores, agentes, agent users, service principals e managed identities não tem a mesma cobertura. A especificação (secção 4) manda verificar cada combinação. Conditional Access também não é revogação instantânea e bloquear autenticação não impede a criação de identidades.

### O que a documentação oficial estabelece

| Tipo de identidade | Cobertura documentada | Fonte |
| --- | --- | --- |
| Utilizadores | Conditional Access normal. Exige Entra ID P1 | `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities` |
| Agent identities | Política com alvo "Agents". Em Graph, `includeAgentIdServicePrincipals`. Não cobre o blueprint quando adquire token para criar identidades, nem a troca intermédia de tokens no `AAD Token Exchange Endpoint: Public` | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id` |
| Agent identities, fluxo OBO | "Selecting an agent identity as the policy subject doesn't cover agent requests made on behalf of the user." | mesma página |
| Agent users | Alvo separado. Uma política sobre a agent identity não se aplica ao agent user e vice-versa. Políticas com atribuição "Users" não se aplicam a agent users | mesma página |
| Service principals (workload identities) | Só single-tenant registados no tenant. Só "Block access". Exige Workload Identities Premium. Atribuição a grupo não é aplicada ao service principal. Avaliada só quando o service principal pede token | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity` |
| Managed identities | "Managed identities aren't covered by policy." A página sugere access reviews como alternativa | mesma página |
| Aplicações multitenant e SaaS de terceiros | Não cobertas pelas políticas de workload identities | mesma página |
| Acesso por chave de API | Contorna por completo o pipeline Entra. Conditional Access não se aplica | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id` |
| Security defaults activos | As políticas de agentes não se aplicam | `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities` |

Limites gerais verificados:

- Conditional Access é avaliado quando o Entra emite ou renova um token. Fonte: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id`.
- As três políticas tenant-wide de bloqueio de agentes impedem autenticação mas não a criação de agent identities. Fonte: `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities`.
- CAE para utilizadores: eventos críticos com latência até 15 minutos, tokens CAE até 28 horas, sem suporte para guests. Fonte: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation`.
- CAE para workload identities: só Microsoft Graph como recurso, só service principals single-tenant LOB, só com a capacidade `cp1` declarada pelo cliente, tokens até 24 horas, managed identities não suportadas. Fonte: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation-workload`.
- Se uma política de workload identities com `ServicePrincipalsInMyTenant` também apanha agent identities: NÃO VERIFICADO.

### Por preencher no tenant

Preencher uma linha por combinação que o piloto usa. Cada célula é por testar.

| Identidade | Recurso chamado | Cliente | Política que se aplica | Estado da política (report-only ou activa) | Testado (sim ou não) | Resultado observado |
| --- | --- | --- | --- | --- | --- | --- |
| Agent identity piloto (S2S) | Graph (SharePoint) | executor | POR PREENCHER NO TENANT | POR PREENCHER NO TENANT | não | POR PREENCHER NO TENANT |
| Agent identity piloto (OBO) | Graph (SharePoint) | POR PREENCHER NO TENANT | POR PREENCHER NO TENANT | POR PREENCHER NO TENANT | não | POR PREENCHER NO TENANT |
| Agent user, se existir | Graph (SharePoint) | POR PREENCHER NO TENANT | POR PREENCHER NO TENANT | POR PREENCHER NO TENANT | não | POR PREENCHER NO TENANT |
| Service principal piloto | Graph (SharePoint) | POR PREENCHER NO TENANT | POR PREENCHER NO TENANT | POR PREENCHER NO TENANT | não | POR PREENCHER NO TENANT |
| Managed identity piloto | Graph (SharePoint) | POR PREENCHER NO TENANT | Não coberta pela documentação | Não aplicável | não | POR PREENCHER NO TENANT |

Campos adicionais a recolher:

- Políticas Conditional Access existentes que mencionem agentes ou service principals, e exclusões.
- Estado de Security defaults.
- Confirmação de que nenhuma política de emergência tenant-wide está habilitada sem análise de impacto e aprovação (especificação, secção 13).
- Se existem agentes que usam chave de API em vez de token Entra.

Forma geral de obter: lista de políticas no centro de administração Entra, relatórios de sign-in filtrados por tipo de identidade, e teste de cada combinação com a política em modo report-only antes de passar a activa.

## 7. Recursos, endpoints, conectores, sessões e caminhos alternativos

### O que a documentação oficial estabelece

- Os scopes Selected exigem três passos: consentimento no Entra, atribuição no recurso com um papel (`read`, `write`, `owner`, `fullcontrol`) e token com o scope. Sem os três, a aplicação não tem acesso. Fonte: `https://learn.microsoft.com/en-us/graph/permissions-selected-overview`.
- O comportamento do scope Selected não muda com o caminho Graph usado. Mesma fonte.
- O download de conteúdo devolve `302` para um URL pré-autenticado em outro anfitrião. O URL dispensa o cabeçalho `Authorization`. A validade é "within minutes" numa página e "1 hour" noutra. Remover permissões "might not immediately invalidate the URL". Fontes: `https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0` e `https://learn.microsoft.com/en-us/graph/api/resources/driveitem?view=graph-rest-1.0`.
- `GET driveItem` devolve `@microsoft.graph.downloadUrl` nos metadados. Inferência: obter metadados entrega também a capacidade de descarregar, a menos que o executor limpe a propriedade.
- `$expand=children` em `GET driveItem` permite listar filhos na mesma chamada. Inferência: a allowlist tem de cobrir parâmetros de consulta.
- `$batch` aceita até 20 pedidos internos, aceita `GET` e `DELETE` no mesmo pedido, e o estado externo `200` não reflecte os internos. Existe em v1.0 e em beta. Fonte: `https://learn.microsoft.com/en-us/graph/json-batching`.
- O APIM não restringe o Graph. Não há documentação que permita limitar o Graph a aceitar pedidos só de um APIM. Inferência: o controlo real é o executor deter a credencial. Fonte: `https://learn.microsoft.com/en-us/azure/api-management/secure-api-management` (excerto) e pesquisa 02.
- Se `/search/query` respeita `Sites.Selected` em modo aplicação: NÃO VERIFICADO em documentação de produto. Existe só uma resposta de fórum a dizer que não.
- Hosts de destino do `302` em SharePoint Online: NÃO VERIFICADO. O exemplo da página é de OneDrive pessoal.
- Revogar um scope de nível superior não remove o acesso dado por um scope inferior. Mesma fonte dos Selected scopes.
- Sessões de utilizador: `revokeSignInSessions` invalida refresh tokens e cookies de sessão, mas não access tokens já emitidos. Fonte: `https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access`.

### Por preencher no tenant

| Campo | Forma geral de obter | Valor |
| --- | --- | --- |
| Site e biblioteca do piloto (site id, drive id, identificador canónico) | Escolha do dono do site, confirmada por consulta Graph à identidade de teste | POR PREENCHER NO TENANT |
| Recurso para teste negativo (outra biblioteca ou outro site) | Escolha do dono do site | POR PREENCHER NO TENANT |
| Endpoints que o runtime do agente consegue alcançar (rede, DNS, egress) | Regras de rede do runtime e do ambiente | POR PREENCHER NO TENANT |
| Conectores e ferramentas ligados ao agente (MCP, Power Platform, outros) | Inventário do runtime e do centro de administração | POR PREENCHER NO TENANT |
| Sessões de utilizador abertas para as identidades do agente | Relatórios de sign-in e sessões activas | POR PREENCHER NO TENANT |
| Credenciais e chaves de API guardadas no runtime ou em variáveis de ambiente | Revisão da configuração do runtime e do cofre | POR PREENCHER NO TENANT |
| Anfitriões reais de destino do download no SharePoint Online | Observação em tráfego de teste | POR PREENCHER NO TENANT |
| Comportamento de `/search/query` com Selected | Teste em tenant | POR PREENCHER NO TENANT |
| Caminhos alternativos descobertos (acesso directo, grants amplos, credenciais partilhadas) | Reconciliação de grants e revisão do runtime. Cada caminho reduz a classificação de cobertura até ser eliminado (especificação, secção 6) | POR PREENCHER NO TENANT |

## 8. Permissões, grupos, RBAC Azure, acessos locais e credenciais partilhadas

### O que a documentação oficial estabelece

- Declarar permissões num blueprint não as concede. Um administrador tem de consentir, no blueprint principal ou em cada agent identity. Fonte: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity`.
- Blueprints não podem ter papéis Azure RBAC. Atribuem-se à agent identity. Mesma fonte.
- Permissões que não podem ser concedidas a agent identities: `User.EnableDisableAccount.All`, `Application.ReadWrite.All`, `Directory.ReadWrite.All`, `AgentIdentityBlueprint.ReadWrite.All`, entre outras. Fonte: `https://learn.microsoft.com/en-us/graph/api/resources/agentid-platform-overview?view=graph-rest-1.0`. Consequência: a identidade de controlo que desactiva identidades não pode ser uma agent identity.
- Identificadores dos scopes Selected e necessidade de consentimento de administrador: `https://learn.microsoft.com/graph/permissions-reference#all-permissions` (excerto). O papel Entra que dá o consentimento de administrador: NÃO VERIFICADO.
- Para atribuir permissões em site, a documentação exige `Sites.FullControl.All`, e em modo delegado "SharePoint Administrator or higher" mais pertença ao site. Fontes: `https://learn.microsoft.com/en-us/graph/permissions-selected-overview` e `https://learn.microsoft.com/en-us/graph/api/site-post-permissions?view=graph-rest-1.0`. A documentação reconhece que o requisito é "necessarily high".
- Exchange: as permissões de RBAC for Applications somam-se às do Entra (união, não intersecção). Uma permissão Graph sem âmbito consentida no Entra anula na prática o âmbito do RBAC. Fonte: `https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac`.
- Conditional Access atribuído a um grupo que contém um service principal não é aplicado a esse service principal. Fonte: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity`.
- Credenciais do blueprint são partilhadas por todas as identidades filhas. Credencial de managed identity tem validade de 90 dias e roda aos 45. Fontes: página de identidade do Agent 365 e FAQ de managed identities.
- Alterações de permissões de managed identities podem demorar várias horas a ter efeito. Fonte: FAQ de managed identities.

### Por preencher no tenant

| Campo | Forma geral de obter | Valor |
| --- | --- | --- |
| Permissões de aplicação e delegadas consentidas, por identidade do piloto | Página de permissões de cada aplicação ou agent identity no centro de administração Entra | POR PREENCHER NO TENANT |
| Presença de grants amplos (`Sites.Read.All`, `Files.Read.All`, `Sites.ReadWrite.All`) em qualquer identidade do piloto | Mesma consulta. Cada ocorrência é drift perigoso | POR PREENCHER NO TENANT |
| Grupos a que cada identidade pertence | Centro de administração Entra | POR PREENCHER NO TENANT |
| Atribuições Azure RBAC por identidade | Controlo de acesso (IAM) dos recursos e subscrição no portal Azure | POR PREENCHER NO TENANT |
| Grants Selected existentes nos recursos (site, lista, item, ficheiro) | Endpoint de listagem de permissões do recurso. Existência e permissão exigida: NÃO VERIFICADO | POR PREENCHER NO TENANT |
| Acessos locais ao recurso (membros e proprietários do site, partilhas, links) | Definições de permissões do site SharePoint | POR PREENCHER NO TENANT |
| Papéis administrativos atribuídos e a quem (Agent ID Administrator, Cloud Application Administrator, Exchange Administrator, SharePoint Administrator) | Atribuições de papéis no centro de administração Entra | POR PREENCHER NO TENANT |
| Credenciais de cada blueprint (tipo, número, validade, quem as detém) | Página do blueprint no centro de administração Entra | POR PREENCHER NO TENANT |
| Credenciais partilhadas entre agentes ou runtimes (incluindo user-assigned managed identity reutilizada) | Inventário de identidades e runtimes | POR PREENCHER NO TENANT |
| Aplicações com Exchange `Mail.*` consentido no Entra (relevante só na fase posterior de rascunhos) | Lista de permissões consentidas e `Test-ServicePrincipalAuthorization` (cmdlet documentado, não cobre grants do Entra) | POR PREENCHER NO TENANT |

## 9. Retenção, residência, backup e recuperação

### O que a documentação oficial estabelece

- Tabelas ledger: não é possível apagar dados antigos em tabelas append-only nem no histórico de tabelas actualizáveis, e `TRUNCATE TABLE` não é suportado. Colunas e tabelas "eliminadas" são renomeadas e ficam fisicamente na base. Uma base ledger não volta a base normal. Fonte: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-limits`.
- Digests: gerados a cada 30 segundos se houver transacções, para o Blob (contentor `sqldbledgerdigests`) ou para o Confidential Ledger. Fonte: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management`.
- Política de imutabilidade do contentor: mínimo de 1 dia e máximo de 146 000 dias. Depois de locked não se apaga e a retenção só pode aumentar, no máximo cinco vezes. `allowProtectedAppendWrites` não muda depois do lock. Fontes: `https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview` e `https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-container-level-worm-policies`.
- A conta de armazenamento não pode ser apagada enquanto houver contentor com política locked ou blobs presentes. Mesmas fontes.
- Blob imutável é incompatível com point-in-time restore. Mesmas fontes.
- Restauro e failover: cada restauro cria nova "incarnation" da base e a verificação usa os digests de todas as incarnations. Num failover group o caminho dos digests não muda. Só se emitem digests de dados já replicados para secundários geográficos. Fonte: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-digest-management`.
- A verificação exige `ALLOW_SNAPSHOT_ISOLATION` activo, pode consumir muitos recursos e a frequência fica a cargo do cliente. Elastic Jobs não é suportado com autenticação Entra-only, resta Azure Automation como opção documentada para agendar. Fontes: `https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-database-verification` e `https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication`.
- Confidential Ledger: hard delete sem recuperação, 2 ledgers por subscrição por omissão, entrada com 8 KiB no máximo, tipo (private ou public) não se altera. Fonte: `https://learn.microsoft.com/en-us/azure/confidential-ledger/limits`.
- Retenção e latência da telemetria do Agent 365: NÃO VERIFICADO.
- O conteúdo do digest inclui `database_name`, `block_id`, `hash`, `last_transaction_commit_time` e `digest_time`. O caminho do blob inclui o nome do servidor e da base. Fonte: página de gestão de digests.

### Por preencher no tenant

| Campo | Forma geral de obter | Valor |
| --- | --- | --- |
| Retenção legal e interna exigida para eventos de auditoria | Política de retenção da organização e parecer do responsável de protecção de dados | POR PREENCHER NO TENANT |
| Período de imutabilidade a aplicar antes do lock (só pode aumentar depois) | Decisão aprovada pelo responsável, registada antes do lock | POR PREENCHER NO TENANT |
| Requisitos de residência de dados e região dos digests | Política de residência. Confirmar o efeito da replicação para o par regional, se for usado o Confidential Ledger | POR PREENCHER NO TENANT |
| Redundância da conta de digests (não LRS) | Decisão de arquitectura | POR PREENCHER NO TENANT |
| Pseudonimização e eliminação de dados pessoais fora da ledger | Parecer do responsável de protecção de dados | POR PREENCHER NO TENANT |
| RPO e RTO aprovados | Aprovação do dono do serviço | POR PREENCHER NO TENANT |
| Redundância e retenção de backup da base | Decisão de arquitectura e política de backup | POR PREENCHER NO TENANT |
| Mecanismo e frequência de verificação da ledger | Decisão: Azure Automation ou outro, aprovado | POR PREENCHER NO TENANT |
| Retenção de logs e telemetria (Log Analytics, Application Insights, Agent 365) | Política de retenção. A do Agent 365 está por documentar | POR PREENCHER NO TENANT |
| Resultado dos ensaios de restauro e failover (BT-17) | Ensaio na fase C | POR PREENCHER NO TENANT |

## 10. Responsáveis e aprovação de risco

### O que a documentação oficial estabelece

- Cada agent identity e cada blueprint exige pelo menos um sponsor. Fonte: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity`.
- Owners de uma agent identity gerem os seus agentes sem os papéis administrativos. Fonte: `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin`.
- Papéis documentados: Agent ID Administrator ou Cloud Application Administrator para gerir agent identities, Agent ID Developer para criar blueprints, Conditional Access Administrator para políticas, Lifecycle Workflows Administrator para lifecycle workflows. Mesma fonte.
- Para as acções de risco, a especificação (secção 13) exige papéis separados (leitor, operador de incidente, administrador de política, auditor), acesso privilegiado temporário quando suportado e break-glass independente da aplicação, documentado e ensaiado.
- A reactivação exige pedido de um administrador de política e aprovação de outro, por desenho do projecto.

### Por preencher no tenant

| Campo | Forma geral de obter | Valor |
| --- | --- | --- |
| Sponsor e owner de cada agent identity e blueprint do piloto | Propriedades do objecto no centro de administração Entra e confirmação humana | POR PREENCHER NO TENANT |
| Contactos de incidente de cada agente | Registo do responsável | POR PREENCHER NO TENANT |
| Titular da aprovação de risco do piloto | Decisão da direcção do serviço | POR PREENCHER NO TENANT |
| Titulares das funções leitor, operador de incidente, administrador de política e auditor | Atribuição formal de papéis | POR PREENCHER NO TENANT |
| Procedimento e titular do break-glass, e data do último ensaio | Documento de break-glass e registo de ensaio | POR PREENCHER NO TENANT |
| Aceitação formal do risco de cada funcionalidade preview usada | Registo de aprovação com alternativa documentada | POR PREENCHER NO TENANT |
| Aprovação dos gates da fase A: âmbito, licenças, permissões | Reunião de revisão e registo da decisão | POR PREENCHER NO TENANT |

## 11. O que este documento bloqueia

Enquanto os campos acima não estiverem preenchidos e aprovados, o gate da fase A não fecha. As lacunas com maior efeito, por ordem de risco:

1. Licença exigida por cada operação e contradição C1 de Conditional Access para agentes.
2. Permissão de menor privilégio para leitura com Selected, e permissão para conceder em lista e item. Ver `permission-matrix.md`.
3. Efeito da desactivação sobre tokens já emitidos, por tipo de identidade. Ver `coverage-matrix.md`.
4. Estado GA ou preview de cada funcionalidade usada e aceitação do risco das que estão em preview.
5. Classificação de dados, retenção e período de imutabilidade, que são irreversíveis depois do lock.
6. Custos incrementais e tiers. Ver `cost-estimate.md`.
