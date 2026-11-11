# Registo de riscos

Projecto: Agent Portifolio. Documento da fase A. Data de verificação das fontes Microsoft: 2026-11-11.

Estado do projecto, sem exagero: a fase A foi escrita a partir de documentação oficial e sem tenant. A fase B (fatia vertical local) está implementada em .NET 8, sem credenciais reais, com o mundo Microsoft simulado. As fases C, D e E não foram executadas. O Bicep está escrito e nunca foi aplicado. Não existe adaptador real para Entra nem para Graph. Nenhum risco deste registo foi aceite formalmente: todas as decisões abaixo estão por tomar.

## 1. Escalas e convenções

Probabilidade e impacto são qualitativos (baixa, média, alta). Não há números, porque não há dados que os sustentem.

- Probabilidade: baixa quer dizer que exige uma sequência invulgar de eventos. Média quer dizer que acontece em operação normal sem controlo adicional. Alta quer dizer que é uma propriedade conhecida da plataforma ou do desenho.
- Impacto: alta quer dizer que pode permitir acesso indevido a dados, ou impedir provar a contenção. Média quer dizer que degrada a operação ou a evidência sem abrir acesso. Baixa quer dizer incómodo recuperável.
- Risco crítico (proposta, a aprovar no gate da fase A): impacto alta com probabilidade média ou alta, sem mitigação provada por teste. O gate da fase E exige zero riscos críticos não mitigados ou aceites formalmente.

Estados usados:

- aberto: sem mitigação provada.
- mitigado no desenho, por provar: o desenho tem o controlo, falta o teste real (fases C ou D).
- bloqueado: o marco correspondente não avança até haver decisão ou prova.
- por decidir: depende só de decisão humana.

Decisão exigida: aceitar, mitigar ou bloquear. "Decisor" fica "por atribuir" em todos os casos: a atribuição é decisão do dono do projecto.

Cada facto Microsoft leva uma chave `Mxx` que remete para a tabela de fontes no fim (URL do Microsoft Learn e data 2026-11-11). Os identificadores `L-nn`, `P-nn` remetem para `gaps-and-assumptions.md`. Os `T-nn` e `BT-nn` remetem para `threat-model.md`.

## 2. Registo

| Id | Descrição | Prob. | Impacto | Mitigação | Estado | Decisão exigida |
|---|---|---|---|---|---|---|
| R-01 | Dependência de funcionalidades preview ou de APIs beta. Em preview segundo a documentação: ID Protection para agentes, editor de manifesto do blueprint, condição "Agent execution environments", lifecycle policies de Agent ID (excerto), agent user no Agent 365 (Frontier). Em beta: políticas de Conditional Access para agentes e logs de sign-in de agentes, desactivação `isDisabled`, remoção de permissões em lista e item. Sem declaração de GA: Selected por nível, RBAC for Applications, ledger em Azure SQL. A Microsoft escreve que preview não tem acordo de nível de serviço e "aren't recommended for production workloads" [M61] [M06] [M04] [M14] [M05] [M12] [M32] [M22]. | média | alta | ADR 0009: nenhuma funcionalidade preview é dependência obrigatória. Cada uma tem alternativa documentada. Aceitação explícita por funcionalidade antes de entrar no caminho crítico. | por decidir | Aceitar por funcionalidade, com a alternativa documentada. Bloquear qualquer marco que dependa de preview sem aceitação. |
| R-02 | Ausência de garantia sobre tokens já emitidos. Desactivar uma identidade e bloquear a emissão não invalida access tokens existentes: "the user loses access when the access token expires" [M01]. Duração por omissão: 60 a 90 minutos [M07] [M08], ou 1 hora [M01]. CAE só cobre alguns recursos e eventos: utilizadores até 28 horas [M02], workload identities até 24 horas, só Graph, só single-tenant de aplicações de linha de negócio e só com `cp1` [M03]. CAE para agentes: NÃO VERIFICADO [M04]. | alta | alta | O bloqueio de novas admissões vem do estado autoritativo no gateway e não da validade do token (T-13). Assumir validade até à expiração (90 minutos de pior caso sem CAE). BT-14 mede o resultado real por recurso. O relatório mostra âmbito explícito e limitação, nunca "acesso universal revogado". `Suspended` nunca passa sozinho a `VerifiedScoped`. | mitigado no desenho, por provar (BT-14 só na fase D) | Aceitar o residual, com âmbito declarado, depois de BT-14. Não aceitar antes de medir. |
| R-03 | Managed identities sem disable documentado e com cache de token. Não existe procedimento de disable por `accountEnabled` para managed identities (NÃO VERIFICADO). A cache de tokens no serviço é de cerca de 24 horas, sem refresh forçado. Tokens de uma identidade eliminada valem até ao fim. Não são cobertas por Conditional Access nem por CAE [M09] [M10] [M03]. A documentação recomenda federar o blueprint numa managed identity, e o efeito sobre a revogação é NÃO VERIFICADO [M14]. | média | alta | Adaptador separado para managed identity. Contenção por paragem ou isolamento do runtime, remoção de grants e, quando aplicável, rotação de credenciais. Não eliminar identidades como primeira acção. Não partilhar a mesma managed identity entre runtimes com âmbitos incompatíveis. A janela de revogação é declarada na cobertura. | aberto | Mitigar (isolamento e remoção de grants). Bloquear o uso de managed identity partilhada como identidade de agente. |
| R-04 | Contradição de licenças. Conditional Access para agentes: uma página exige Entra ID P1, a mais recente exige Microsoft 365 E7 ou Agent 365 com Entra P1 ou Microsoft 365 E3. ID Protection para agentes: P2 durante o preview, "starting soon" Agent 365. Preços e licença por operação: NÃO VERIFICADO [M04] [M05] [M06] [M62]. | alta | média | Tratar Agent 365 como requisito até prova em contrário. Documentar a divergência. Estimativa de custos sem valores, só com a natureza do custo e a página oficial de preços. Confirmar no tenant. | aberto | Bloquear a fase C até as licenças serem confirmadas e aprovadas (gate da fase A: licenças). |
| R-05 | Imutabilidade contra RGPD. Tabelas ledger append-only não permitem apagar dados ("Deleting older data in append-only ledger tables ... isn't supported", `TRUNCATE` não suportado) [M42]. Política de imutabilidade `locked` não se apaga, só se aumenta, e a conta não se apaga enquanto houver contentor com política `locked` [M46]. `isLedgerOn` não se pode reverter [M42]. A documentação lida não trata RGPD. Não há conformidade RGPD automática por usar serviços Microsoft. | média | alta | Nenhum dado pessoal desnecessário na ledger nem no armazenamento imutável. A ledger guarda digest SHA-256 dos argumentos e referências minimizadas. Pseudonimização, retenção e eliminação decididas antes do lock. Retenção decidida antes do lock porque depois só aumenta. Decisão sobre `isLedgerOn` antes de criar a base (P-11). | bloqueado (lock e base de dados ledger) | Bloquear o lock da política e a criação da base ledger até decisão humana com o responsável de protecção de dados. |
| R-06 | Contorno do gateway por quem edita políticas APIM. Quem tem permissão de edição pode usar `authentication-managed-identity` para autenticar-se como a identidade do serviço, extrair ou reencaminhar o token, e o APIM "does not validate which backend the token is sent to" [M37]. A política de validação pode referenciar certificados disponíveis [M35]. Não há mecanismo documentado para restringir o Graph a pedidos de um APIM (L-32). | média | alta | A credencial Graph fica no executor e não no APIM. O executor valida token e estado autoritativo por si [M38]. Azure RBAC do APIM separado do plano de dados. Políticas só por IaC com revisão e CI. Alerta de alteração administrativa sensível. Em caso de dúvida, o controlo real é o agente não ter credencial Graph (BT-06). | mitigado no desenho, por provar (BT-05 e BT-06 na fase C) | Mitigar. Aceitar o residual para quem controla a plataforma (fronteira F8). Não aceitar sem separação de papéis no Azure. |
| R-07 | Contradição sobre o âmbito mínimo das operações de leitura. A visão geral de Selected diz que `Sites.Selected` com papel `read` serve os caminhos de leitura. As quatro páginas de leitura listam `Files.Read.All` como menor privilégio, de âmbito de tenant inteiro (L-19). Se a visão geral não se confirmar, o MVP com âmbito estreito não é viável. | média | alta | O desenho assume `Sites.Selected` com `read` (inferência). Teste endpoint a endpoint e teste negativo noutro recurso na fase C. Nunca usar `Sites.Read.All`, `Sites.ReadWrite.All` ou `Files.Read.All` para contornar o protótipo. | bloqueado (implementação real de leitura) | Bloquear a implementação real das quatro operações até a matriz estar completa no tenant (a especificação bloqueia linhas incompletas). |
| R-08 | URL pré-autenticado de download. `GET .../content` devolve 302 para um URL que não exige autenticação [M25]. A remoção de permissões "might not immediately invalidate the URL" [M28]. Validade: minutos [M25] ou 1 hora [M28]. | média | média | O executor segue o redireccionamento, devolve só bytes, não devolve nem regista o `Location` nem o URL, e remove `downloadUrl` dos metadados. Pior caso de 1 hora na análise. Lista de anfitriões de saída do executor. | mitigado no desenho, por provar (fase C) | Aceitar o residual (janela até 1 hora por URL já emitido) e declará-lo na documentação de contenção. |
| R-09 | Contorno de listas de caminhos por `$batch`, `$expand` e redireccionamentos. Batch: 20 pedidos por chamada, método externo sempre POST, `200` externo não mostra os resultados internos [M29]. `$expand` traz filhos na mesma chamada [M26]. | média | alta | Sem operação batch no catálogo. O executor monta os pedidos. O APIM nega `/$batch` (v1.0 e beta). Argumentos fechados, sem `$expand` nem `$select`. TC-06. | mitigado no desenho, por provar (TC-06 local, BT-05 fase C) | Mitigar. Qualquer pedido composto futuro exige validação de cada pedido interno contra a mesma lista. |
| R-10 | União de permissões Entra com RBAC for Applications do Exchange. "The permissions assigned using Application RBAC act in addition to grants you make in Microsoft Entra ID." Uma permissão `Mail.Read` sem âmbito no Entra anula o âmbito do RBAC [M32]. Não existe papel só de rascunhos [M32]. Propagação de 30 minutos a 2 horas. Fora do MVP. | média | alta | Verificar as duas autoridades (Entra e Exchange) antes de classificar uma capacidade de correio como com âmbito. Remover permissões organizacionais do Entra. Restringir operações no executor. Não usar Application Access Policies em implementação nova [M33]. | bloqueado (correio, fase posterior) | Bloquear correio até existir verificação das duas autoridades e decisão sobre o estado de GA do RBAC (L-28). |
| R-11 | Dois interruptores de bloqueio sem sincronização documentada: o bloqueio no centro de administração Microsoft 365 (Agent 365) e a desactivação no Entra. Se um altera o outro é NÃO VERIFICADO [M64]. | média | média | O relatório de contenção trata cada interruptor como um passo separado, com verificação por sonda própria. Teste no tenant se Agent 365 estiver licenciado. | aberto | Mitigar. Decidir se Agent 365 entra no âmbito. |
| R-12 | Acessos alternativos do agente ao recurso. Grants directos no agente, credenciais partilhadas, caminhos locais, ou runtime com rota para Graph. A carteira não consegue retirar sozinha um grant externo usado por um caminho directo. | média | alta | Cobertura classificada (`GatewayEnforced`, `DirectAccessPossible`, `Unverified`). Grants no agente baixam a cobertura. Perfil forte remove acessos directos e restringe egress, como defesa adicional. Reconciliação periódica (T-12). | mitigado no desenho, por provar (BT-05 e BT-06 na fase C) | Bloquear a classificação `GatewayEnforced` até prova de impossibilidade de acesso directo no perfil forte. |
| R-13 | Credenciais do blueprint partilhadas por todas as identidades filhas. O comprometimento do blueprint afecta todas. Desactivar o blueprint afecta todas [M14] [M06]. | baixa | alta | Cada blueprint é uma fronteira de credencial. Preferir credencial federada a segredo. Desactivar blueprint exige aprovação e âmbito distintos. Desactivação por omissão do agente individual. | aberto | Mitigar. Decidir quantos agentes por blueprint no piloto. |
| R-14 | Confiança no destino dos digests. Um administrador com acesso ao destino pode adulterar digests e dados em conjunto [M39]. Ledger dá evidência de adulteração, não prevenção. | baixa | alta | Blob com política `locked` e escrita só por identidade do servidor. Privilégios SQL, destino e verificação separados. Verificação periódica e após restauro. Azure Confidential Ledger como opção com garantia mais forte contra administradores privilegiados [M41], com limites próprios [M49]. | por decidir | Decidir entre Blob imutável e Confidential Ledger como destino. Aceitar o residual do Blob se o modelo de ameaça não incluir administradores da subscrição. |
| R-15 | Custos desconhecidos. A natureza do custo está descrita. Os valores não: APIM por escalão, SQL, Confidential Ledger, Key Vault e managed identities são NÃO VERIFICADOS em parte. Política `locked` impede libertar capacidade antes do fim da retenção [M46]. | média | média | Estimativa de custos só com a natureza do custo e a página oficial de preços, sem valores. Justificar escalões, rede e suporte antes de provisionar. | por decidir | Bloquear o provisionamento até haver estimativa aprovada. |
| R-16 | Simulação confundida com prova. O mundo Microsoft é simulado na fase B. BT-05, BT-06, BT-14, BT-17 e BT-20 só ficam provados nas fases C e D. Uma resposta 200, um painel verde ou uma demonstração com mocks não provam contenção. | média | alta | Simulação assinalada em todo o código e documentação. Estado dos BT explícito no threat model. Relatório de contenção distingue confirmação administrativa de confirmação por sonda. | mitigado no desenho | Mitigar. Nenhum relatório pode afirmar contenção sem prova das fases C e D. |
| R-17 | Pressupostos sem tenant. Formato dos identificadores, claims `tid` e `oid`, comportamento de `accountEnabled`, anfitriões do 302 (P-01, P-02, P-07, P-09). | média | média | Documentados como pressupostos. Falham fechado quando errados (recusar). Validação na fase C e D. | bloqueado (fase C) | Bloquear a fase C até haver tenant. Validar cada pressuposto e actualizar `gaps-and-assumptions.md`. |
| R-18 | Operação da fila de dead-letter e da outbox. A DLQ do Service Bus não se limpa sozinha [M52]. A reentrega é pelo menos uma vez [M50]. Uma mensagem retida em DLQ é uma contenção por tratar. | média | média | Alerta sobre profundidade da DLQ. Processo de tratamento no runbook. Consumidores idempotentes e revalidação de epoch. | mitigado no desenho, por provar (fase C) | Mitigar. Definir quem trata a DLQ. |
| R-19 | Agendamento da verificação da ledger com autenticação Entra-only. Elastic Jobs não é suportado com Entra-only [M53], e a verificação é pesada em bases grandes [M40]. | média | média | Azure Automation ou job próprio, a decidir. Frequência escolhida pelo cliente [M40]. Identidade de verificação só com `VIEW LEDGER CONTENT`. | por decidir | Decidir o mecanismo de agendamento e a frequência. |
| R-20 | Operador ou PolicyAdmin comprometido, ou conluio de dois PolicyAdmin. Um administrador de SQL ou Azure fora da aplicação não é travado por ela. | baixa | alta | Papéis separados. Reactivação por dois PolicyAdmin distintos. Iniciador humano em cada evento. Acesso privilegiado temporário quando licenciado. Ledger e digests para evidência. | mitigado no desenho, por provar (BT-19 local, TC-09) | Aceitar o residual de conluio. Exigir autenticação forte no tenant. |
| R-21 | Restauro ou failover repõe um estado em que o agente estava activo. | baixa | alta | BT-17. Reconciliação e validação de RPO antes de reabrir tráfego. Verificação da ledger após restauro. Digests só de dados replicados [M41]. Ensaio de restauro e failover. | mitigado no desenho, por provar (fase C) | Mitigar. Aprovar RPO e RTO antes da fase E. |
| R-22 | Throttling e bloqueio de aplicação no SharePoint. 429 ou 503 com `Retry-After` obrigatório. O SharePoint pode bloquear a aplicação ou padrões de pedido. Limites dependem das licenças. Testes de carga não são permitidos [M31]. A documentação avisa para não dividir uma função por vários AppIDs para fugir aos limites [M31], e o desenho tem uma identidade de execução por agente (inferência de tensão, a validar no piloto). | média | média | Adaptadores e executor respeitam `Retry-After`. Estado parcial visível em throttling. User-Agent decorado. Limite por hora por agente. Medir no piloto, sem carga artificial sobre SharePoint. | mitigado no desenho, por provar (fase C) | Aceitar o residual. Confirmar no piloto que identidades por agente não degradam os limites do tenant. |
| R-23 | Disponibilidade do SQL é a disponibilidade do produto. A falha fechada nega novas acções quando o estado autoritativo não está disponível (T-16). | média | média | Escolha deliberada. Alta disponibilidade e geo-replicação da base. Alertas. | mitigado no desenho | Aceitar, com SLO de disponibilidade a definir. |
| R-24 | Telemetria Agent 365 falha em silêncio: `200 OK` com spans rejeitados (`tenant_not_licensed`), só `invoke_agent` no centro de administração, retenção e latência NÃO VERIFICADAS [M15]. | média | baixa | A observabilidade Agent 365 complementa e não substitui a ledger. Verificar rejeições nos resultados por span. | aberto | Aceitar. Não usar como evidência primária. |
| R-25 | Owners das ameaças e dos riscos por atribuir. Sem owner, uma mitigação não tem responsável. | alta | média | Cada ameaça indica o papel sugerido. | por decidir | Bloquear o gate da fase A até atribuir. |

## 3. Riscos que bloqueiam marcos

| Risco | Marco bloqueado | Razão |
|---|---|---|
| R-04 | Fase C | Licenças por confirmar. |
| R-05 | Lock da política de imutabilidade e base ledger | Decisão de protecção de dados. |
| R-07 | Implementação real das operações de leitura | Matriz de permissões incompleta. |
| R-10 | Correio com âmbito (fase posterior) | Verificação das duas autoridades. |
| R-12 | Classificação `GatewayEnforced` | Prova de impossibilidade de acesso directo. |
| R-15 | Provisionamento | Estimativa de custos por aprovar. |
| R-17 | Fase C | Sem tenant. |
| R-25 | Gate da fase A | Owners por atribuir. |

## 4. Fontes

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
| M12 | `entra/identity/enterprise-apps/deactivate-app-registration` | inteira |
| M14 | `microsoft-agent-365/developer/identity` | inteira |
| M15 | `microsoft-agent-365/developer/observability-concepts` | inteira |
| M22 | `graph/permissions-selected-overview` | inteira |
| M25 | `graph/api/driveitem-get-content?view=graph-rest-1.0` | inteira |
| M26 | `graph/api/driveitem-get?view=graph-rest-1.0` | inteira |
| M28 | `graph/api/resources/driveitem?view=graph-rest-1.0#instance-attributes` | excerto |
| M29 | `graph/json-batching` | inteira |
| M31 | `sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online` | inteira |
| M32 | `exchange/permissions-exo/application-rbac` | inteira |
| M33 | `exchange/permissions-exo/application-access-policies` | inteira |
| M35 | `azure/api-management/validate-azure-ad-token-policy` | inteira |
| M37 | `azure/api-management/api-management-howto-use-managed-service-identity#security-considerations-for-managed-identities` | excerto |
| M38 | `entra/msidweb/advanced/api-gateways#follow-best-practices` | excerto |
| M39 | `sql/relational-databases/security/ledger/ledger-overview` | inteira |
| M40 | `sql/relational-databases/security/ledger/ledger-database-verification` | inteira |
| M41 | `sql/relational-databases/security/ledger/ledger-digest-management` | inteira |
| M42 | `sql/relational-databases/security/ledger/ledger-limits` | inteira |
| M46 | `azure/storage/blobs/immutable-storage-overview` | inteira |
| M49 | `azure/confidential-ledger/limits` | inteira |
| M50 | `azure/service-bus-messaging/service-bus-message-loss-and-duplicates` | excerto |
| M52 | `azure/service-bus-messaging/service-bus-dead-letter-queues` | inteira |
| M53 | `azure/azure-sql/database/authentication-azure-ad-only-authentication` | inteira |
| M61 | `entra/id-governance/agent-lifecycle-policies` | excerto |
| M62 | `entra/fundamentals/licensing` | excerto |
| M64 | `microsoft-agent-365/admin/manage-agents` | excerto |
