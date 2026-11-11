# Threat model

Projecto: Agent Portifolio. Documento da fase A, secção 14 da especificação. Data de verificação das fontes Microsoft: 2026-11-11.

Estado do projecto, sem exagero: a fase A (validação) foi escrita a partir de documentação oficial e sem tenant. A fase B (fatia vertical local) está implementada em .NET 8, sem credenciais reais, e o mundo Microsoft é simulado. As fases C, D e E não foram executadas. O Bicep está escrito e nunca foi aplicado. Não existe nenhum adaptador real para Entra nem para Graph. Tudo o que depende do tenant fica marcado "POR VALIDAR NO TENANT".

## 1. Convenções

Cada afirmação sobre a Microsoft leva uma chave `Mxx` que remete para a tabela de fontes no fim do documento, com URL do Microsoft Learn e data de verificação 2026-11-11.

Três níveis de certeza, sempre separados:

- Verificado em documentação: o texto oficial afirma-o.
- Inferência: conclusão deste projecto a partir de factos verificados. A Microsoft não a afirma.
- Por validar no tenant: não pode ser decidido sem tenant. Nada disto é tratado como facto.

"NÃO VERIFICADO" quer dizer que a documentação lida não o afirma. Não quer dizer que seja falso.

Identificadores de teste:

- `BT-01` a `BT-20` são os testes bloqueantes da secção 15 da especificação, pela mesma ordem. Lista na secção 5 deste documento.
- `TC-nn` são testes que a especificação não cobre. O número `nn` é o da ameaça (T-nn). Lista na secção 6. Os `TC` locais existem em parte, como diz cada ameaça; os de fase C e D não existem.

Estado local dos BT, confirmado contra a suite em 2026-11-11 (78 testes, 78 passados com `AP_TEST_SQL` definido contra SQL Server 2022 real, 73 passados e 5 ignorados sem ela): BT-05, BT-17 e BT-20 só ficam provados nas fases C e D. BT-06, BT-13 e BT-14 só existem em simulação. Localmente existe no máximo uma aproximação simulada, que NÃO conta como prova. Os restantes têm prova local, sobre o mundo simulado, e BT-08, BT-10 e BT-16 têm também teste contra SQL Server 2022 real. O detalhe, com os nomes dos testes, está em `test-plan.md`.

Os nomes de componentes seguem o código de `src/Core` (`AuthorizationEngine`, `OperationCatalog`, `ResourceCanonicalizer`, `ExecutionService`, `IWalletStore.TransactAsync`, `IDocumentGateway`, `IContainmentAdapter`, `ReasonCodes`, `EventTypes`). `ControlService`, `ContainmentOrchestrator` e `ReconciliationService` existem agora em `src/Core/Services`.

Owners: nenhum foi atribuído. Cada ameaça indica "por atribuir" com o papel sugerido. A atribuição é decisão do dono do projecto e faz parte do gate da fase A.

## 2. Fronteiras de confiança

Fonte: secção 5 da especificação. A fronteira F8 é uma inferência da secção 13 (papéis separados, IaC, acesso privilegiado) e não consta da lista da secção 5.

| Id | Fronteira | Quem está de cada lado | Regra de desenho |
|---|---|---|---|
| F1 | Agente para gateway | Agente e runtime (não confiáveis) contra APIM | O agente não é confiável para autorização, identidade declarada nem auditoria. O chamador vem de token validado. |
| F2 | Gateway para executor | APIM contra PolicyExecutor | O backend não pode ser contornável. O executor valida também o token e lê o estado autoritativo. |
| F3 | Executor para Microsoft Graph | PolicyExecutor contra Graph e SharePoint | A credencial de dados fica no executor. O agente nunca a recebe. Uma identidade de execução por agente. |
| F4 | Operador para API de controlo | Pessoas contra ControlApi | Autenticação Entra e papéis Reader, IncidentOperator, PolicyAdmin, Auditor. A identidade de controlo não é usada pelo runtime do agente. |
| F5 | Serviços para SQL | ControlApi, PolicyExecutor e ContainmentWorker contra SQL | Estado, políticas, outbox e ledger. Todas as consultas levam o tenant. Uma única porta transaccional. |
| F6 | Worker para o mundo externo | ContainmentWorker contra Entra, runtime e recursos | Adaptadores idempotentes, com timeout, retentativas limitadas e sonda de verificação. |
| F7 | SQL para digests | Base de dados contra armazenamento independente de digests | Os digests só valem se o destino não for adulterável por quem tem privilégios na base de dados [M41]. |
| F8 | Plano de gestão | Quem edita Azure RBAC, políticas APIM, Bicep e CI contra tudo o resto | Fora da aplicação. A aplicação não consegue defender-se de quem controla a plataforma. Só a ledger e os digests dão evidência. |

Diagrama de texto:

```text
 NÃO CONFIÁVEL                |            CONFIÁVEL (controlado pela equipa)
                              |
 [Agente + runtime]           |
        |  token Entra        |
        v                     |
       (F1)                   |
        |                     |
        +-------------------> [APIM] --(F2)--> [PolicyExecutor] --(F3)--> [Microsoft Graph / SharePoint]
                              |                       |                     (credencial só aqui;
                              |                       | (F5)                 302 para URL pré-autenticado
                              |                       v                      noutro anfitrião)
 [Operador] --Entra--(F4)---> [ControlApi] --(F5)--> [SQL: estado, outbox, ap.AuditEvent (ledger)]
                              |                       |                |
                              |                       | outbox         | (F7) digests
                              |                       v                v
                              |              [ContainmentWorker]   [Armazenamento imutável]
                              |                       |
                              |                       +--(F6)--> [Entra | runtime | grants no recurso]
                              |
 F8: plano de gestão (Azure RBAC, políticas APIM, Bicep, CI) atravessa todos os blocos confiáveis
```

Duas consequências que o diagrama obriga a ter presentes.

Primeira: o destino final do tráfego de dados é o Graph, um serviço público da Microsoft. Os mecanismos de rede documentados para APIM (rede virtual, private endpoint, lista de IPs, mTLS) aplicam-se ao troço gateway para executor [M55]. A documentação lida não mostra forma de restringir o Graph a aceitar pedidos só de um APIM (inferência). O que impede o contorno do gateway é o agente não possuir credencial Graph.

Segunda: o conteúdo de um ficheiro descarregado segue um redireccionamento 302 para um URL pré-autenticado noutro anfitrião [M25]. Esse tráfego não passa pelo APIM quando é o chamador a segui-lo. Por isso quem segue o redireccionamento é o executor.

## 3. Ameaças

Cada entrada tem: fronteira, pré-condição, mitigação (o que o desenho faz, com o componente), teste, evidência, risco residual e owner.

### T-01 Prompt injection

- **Fronteira:** F1 (o agente é não confiável), com efeito em F2 e F3.
- **Pré-condição:** um documento lido ou uma entrada do utilizador contém instruções que levam o agente a pedir operações ou recursos fora do âmbito aprovado.
- **Mitigação:** a decisão de acesso não usa modelo. O `AuthorizationEngine` é determinístico, sem I/O, sem cache e sem relógio próprio. O `OperationCatalog` é uma lista fechada com duas operações (`sharepoint.document.read` e `sharepoint.document.list`). Os argumentos têm de ser exactamente os esperados (nomes e número). Não há URL, caminho nem parâmetro extra. A capacidade liga agente, acção e recurso canónico. O limite por hora (`WalletLimits.MaxActionsPerHour`) trava volume. O sistema só estreita o que a autoridade externa permite, nunca alarga.
- **Teste:** BT-01 (recurso não autorizado falha). TC-01 em parte (local): `Unknown_missing_or_extra_arguments_are_rejected`, `Item_identifiers_cannot_carry_paths_or_queries` e `Generic_proxy_batch_and_arbitrary_urls_are_impossible` cobrem URL, `..` e parâmetros em excesso. Texto de instruções embutido e a prova de que `IDocumentGateway` não é chamado não têm teste dedicado.
- **Evidência:** evento `action.denied` com `reason_code`, e ausência de `action.admitted` para o mesmo `correlation_id`.
- **Risco residual:** o produto não inspecciona o conteúdo devolvido ao agente. Uma injecção que peça só o que o agente já pode ler dentro do âmbito autorizado é executada, e o resultado pode ser reencaminhado pelo agente (ver T-20). O limite por hora reduz volume e não impede o primeiro pedido.
- **Owner:** por atribuir (papel sugerido: responsável de segurança aplicacional, com o owner do agente).

### T-02 Roubo de token

- **Fronteira:** F1 e F3. Três credenciais distintas: token Entra do agente para o gateway, credencial de dados do executor, e URL pré-autenticado de download.
- **Pré-condição:** o atacante obtém o token do agente (memória ou logs do runtime) ou a credencial do executor.
- **Mitigação:** o agente nunca recebe a credencial Graph, que fica no `PolicyExecutor`. O token do agente só serve para o gateway. O APIM valida o token com `validate-azure-ad-token` e o modelo de política obriga a `audiences` e a aplicação cliente esperada, porque só o `tenant-id` é obrigatório e uma política só com ele aceitaria qualquer token do tenant (inferência) [M35]. O executor valida o token outra vez, porque a Microsoft recomenda validar sempre no backend e avisa que "tokens can be replayed" [M38]. Há uma identidade de execução por agente (`ExecutorObjectId`), por isso uma credencial de executor roubada fica limitada ao âmbito de um agente. A capacidade interna é referência opaca e não token ao portador. A ledger nunca guarda tokens (BT-18). A suspensão bloqueia a admissão pelo estado autoritativo, mesmo com token ainda válido.
- **Teste:** BT-02 (chamador e tenant falsificados falham), local. BT-06 (agente sem credencial alternativa para Graph) e BT-14 (token previamente emitido testado e resultado real reportado) só nas fases C e D. TC-02 a criar (fase C): token válido do tenant com outra audiência ou de outra aplicação cliente recebe 401 no APIM e é recusado no executor.
- **Evidência:** `action.denied` com `agent_suspended` mais `alert.post_suspension_attempt`; resultado medido de BT-14 por recurso e por tipo de identidade.
- **Risco residual:** a documentação não garante invalidar access tokens já emitidos. Desactivar e revogar impede tokens novos, e para aplicações que usam access tokens "the user loses access when the access token expires" [M01]. A duração por omissão tem fontes em desacordo: 60 a 90 minutos, média 75 [M07] [M08], contra 1 hora [M01]. Usar 90 minutos como pior caso sem CAE. Com CAE os tokens podem durar até 28 horas para utilizadores [M02] e até 24 horas para workload identities [M03]. CAE para workload identities só cobre Microsoft Graph, service principals single-tenant de aplicações de linha de negócio, e exige o cliente declarar `cp1` [M03]. Managed identities não são suportadas por CAE e têm cache de token de cerca de 24 horas, sem refresh forçado [M03] [M09]. Se CAE cobre agent identities: NÃO VERIFICADO [M04]. O que o gateway nega após suspensão continua válido no Entra até expirar, e contra o Graph directo o token roubado funciona se o agente tiver grant directo (cobertura `DirectAccessPossible`).
- **Owner:** por atribuir (papel sugerido: arquitecto de identidade, com o administrador Entra).

### T-03 Replay

- **Fronteira:** F1, F2 e F5. Três replays diferentes: do token no gateway, do pedido `POST /actions`, e de mensagens de contenção.
- **Pré-condição:** o atacante, ou uma falha de rede, repete um pedido ou mensagem já visto.
- **Mitigação:** pedido repetido: a chave de idempotência é obrigatória e valida-se por formato. Mesma chave e mesmos argumentos devolvem o resultado anterior sem repetir o efeito e sem payload. Mesma chave com argumentos diferentes dá `idempotency_conflict`. Token repetido: o estado autoritativo é lido de novo em cada admissão, por isso um token capturado antes da suspensão não passa depois dela. Antes da suspensão, um replay do token é indistinguível de uso legítimo, porque o token é ao portador. Mensagens: ver T-14.
- **Teste:** BT-10 (repetição de mensagem não duplica efeito) e BT-03 (suspensão invalida capacidade anterior ainda dentro da validade), locais. TC-03 a criar (fase C): token capturado antes da suspensão e reutilizado depois recebe `agent_suspended` através do APIM real.
- **Evidência:** `idempotent_replay` e `in_progress` nas respostas, `Attempt` no registo da acção, `alert.post_suspension_attempt`.
- **Risco residual:** com o agente activo, um replay dentro da validade do token executa operações permitidas. O limite por hora e a idempotência reduzem o efeito. A Microsoft avisa que as configurações de gateway podem mudar e os tokens podem ser repetidos [M38].
- **Owner:** por atribuir (papel sugerido: engenheiro de plataforma responsável pelo PolicyExecutor).

### T-04 Confused deputy

- **Fronteira:** F2 e F3. O executor é o deputy: tem uma credencial de dados que o agente não tem.
- **Pré-condição:** um agente consegue que o executor use a sua credencial para um recurso ou agente que não lhe pertence.
- **Mitigação:** a identidade do chamador vem do token validado (`tid` e `oid`), nunca de `agent_id` no corpo (`CallerIdentity`). O agente resolve-se por tenant e identidade (`FindAgentByIdentityAsync`). A capacidade só vale para o seu tenant, agente, acção e recurso canónico. O executor usa o `ExecutorObjectId` do próprio agente, sem executor universal com direitos de tenant inteiro. Em SharePoint, o grant Selected existe no recurso e a identidade que atribui permissões é separada do executor [M22] [M23].
- **Teste:** BT-02 e BT-07 (retirar uma capacidade não altera outras), locais. TC-04 a criar (local): o agente A pede o recurso de um agente B com a sua própria capacidade e recebe `capability_missing`.
- **Evidência:** `action.denied` com `capability_missing` ou `agent_unknown`. `executorIdentity` registado em cada `action.admitted`.
- **Risco residual:** a segurança depende de o registo associar correctamente identidade e `ExecutorObjectId`. Um erro humano nesse registo cria um deputy confuso. A reconciliação (T-12) detecta grants inesperados, não registos errados. Os claims `tid` e `oid` do token de um agente Entra são pressupostos: POR VALIDAR NO TENANT.
- **Owner:** por atribuir (papel sugerido: arquitecto de segurança).

### T-05 SSRF

- **Fronteira:** F2 e F3.
- **Pré-condição:** o agente consegue influenciar para onde o executor faz pedidos HTTP (URL, anfitrião, caminho, `@odata.nextLink`, `Location`).
- **Mitigação:** não existe proxy HTTP genérico. O chamador não fornece URL: só `siteId`, `driveId` e `itemId`, com formato fechado, e o executor constrói o pedido Graph a partir deles. Não se normaliza: o `ResourceCanonicalizer` rejeita o que não é canónico. Redireccionamentos e ligações de paginação são seguidos só pelo executor e só depois de validados contra uma lista de anfitriões (a implementar na fase C). O executor corre sem rota para endereços internos que não precise.
- **Teste:** TC-05 em parte. Parte local: argumentos que pareçam URL são recusados (`Generic_proxy_batch_and_arbitrary_urls_are_impossible`). Endereço IP e anfitrião não têm teste dedicado. Parte fase C: o executor não consegue ligar a destinos fora da lista (teste de saída de rede). BT-05 (gateway e backend não contornáveis) só na fase C.
- **Evidência:** `action.denied` com `resource_not_canonical`; registo de saída de rede do runtime do executor na fase C.
- **Risco residual:** os anfitriões de destino do 302 em SharePoint Online são NÃO VERIFICADOS. O exemplo da página usa um anfitrião de OneDrive pessoal [M25]. `@odata.nextLink` é um URL absoluto na paginação [M27], e tem de ser validado antes de seguir (inferência). Se o executor seguir links sem validação, a ameaça reabre.
- **Owner:** por atribuir (papel sugerido: engenheiro de plataforma, com revisão de segurança).

### T-06 Batch e redirect bypass

- **Fronteira:** F2 e F3.
- **Pré-condição:** o agente usa um pedido composto, um parâmetro de consulta ou um redireccionamento para chegar a operações que a lista não permite.
- **Mitigação:** o `OperationCatalog` não tem operação batch. O executor monta os pedidos Graph e nunca reencaminha o corpo do agente. O APIM nega `/$batch` em `v1.0` e em `beta` por omissão (política a escrever na fase C). O batch tem limite de 20 pedidos internos, o método externo é sempre POST e um `200` externo não mostra o resultado dos internos [M29]. Isto contorna uma lista de caminhos e métodos feita no gateway (inferência). Parâmetros como `$expand` e `$select` não são aceites, porque `driveItem` permite `$expand` para trazer filhos [M26] e os metadados trazem `@microsoft.graph.downloadUrl` [M28]. Redireccionamento: o executor segue o 302 de `/content` [M25], devolve só os bytes, nunca devolve `Location` nem regista o URL.
- **Teste:** TC-06 em parte (local): `$batch` e argumentos em excesso são recusados (`Generic_proxy_batch_and_arbitrary_urls_are_impossible`) e a resposta não contém `downloadUrl` (`BT06_`). `$expand`, `$select` e `Location` não têm teste dedicado. BT-05 (fase C): chamada directa ao executor e batch pelo gateway falham.
- **Evidência:** `action.denied` com `operation_not_allowlisted` ou `arguments_invalid`. Logs do executor sem URL pré-autenticado.
- **Risco residual:** o URL pré-autenticado já emitido não é invalidado de imediato quando se remove a permissão ("might not immediately invalidate the URL") [M28]. A validade tem duas fontes em desacordo: "might expire within minutes" [M25] contra "1 hour" [M28]. Usar 1 hora como pior caso. Quem tiver o URL descarrega sem token. O limite de tamanho do corpo do batch é NÃO VERIFICADO [M29].
- **Owner:** por atribuir (papel sugerido: engenheiro de plataforma, com o responsável do APIM).

### T-07 Recursos canónicos manipulados

- **Fronteira:** F1 e F2.
- **Pré-condição:** o chamador envia o mesmo recurso com outra grafia (maiúsculas, codificação percentual, espaços, barra final, Unicode), um caminho em vez de um identificador, ou identificadores de sites diferentes combinados.
- **Mitigação:** o `ResourceCanonicalizer` só aceita recursos já canónicos e não normaliza. Uma entrada que precise de corte, mudança de caixa ou descodificação é rejeitada, porque duas grafias do mesmo recurso são um erro de autorização. As expressões regulares são fechadas e ancoradas. A comparação entre `Capability.Resource` e o recurso canónico resolvido é ordinal e exacta. O endereçamento é só por identificadores, nunca por caminho ou URL. O grant Selected no recurso é segunda barreira: sem atribuição explícita "an application consented ... would initially have no access" [M22].
- **Teste:** BT-01 (recurso autorizado funciona, não autorizado falha). TC-07 em parte (local): `Non_canonical_resources_are_rejected_not_normalised` cobre caixa, espaços, `%2F`, `..`, URL, recurso incompleto e mudança de linha. Homóglifos e barra final não estão na tabela.
- **Evidência:** `action.denied` com `resource_not_canonical`. O digest SHA-256 dos argumentos permite provar quais argumentos foram pedidos sem os guardar.
- **Risco residual:** o formato dos identificadores de site, drive e item é pressuposto do desenho (site: `anfitrião,GUID,GUID` em minúsculas; drive e item: texto opaco com `A-Za-z0-9!_-`). A documentação lida não o fixa. POR VALIDAR NO TENANT. Se o formato real diferir, a falha esperada é recusar tudo (falha fechada), que bloqueia o uso mas não o abre.
- **Owner:** por atribuir (papel sugerido: engenheiro de plataforma).

### T-08 Escalada entre tenants

- **Fronteira:** F1, F2 e F5.
- **Pré-condição:** um token ou identificador de outro tenant é aceite, ou uma consulta devolve dados de outro tenant.
- **Mitigação:** o `tid` do token validado tem de coincidir com o tenant do agente. Todas as consultas de `IWalletTx` levam `tenantId`: um UUID não é autorização. A única consulta que cruza tenants é `ListPendingOutboxAsync`, reservada ao worker. Agent identities são single-tenant e só recebem tokens no tenant onde foram criadas, enquanto os blueprints podem ser multitenant [M14]. O âmbito de qualquer acção sobre blueprint tem de ser explícito (ver T-10).
- **Teste:** BT-02 (chamador e tenant falsificados falham), local. TC-08 (local): `Tenants_are_isolated_for_operators_and_for_callers` e `Operator_of_another_tenant_cannot_see_or_touch_the_agent`. Um chamador com `(tid, oid)` sem agente dá `agent_unknown`, e um operador de outro tenant recebe `NotFoundException`, o mesmo que um agente inexistente.
- **Evidência:** `action.denied` com `agent_unknown`.
- **Risco residual:** o piloto tem um tenant. O isolamento multitenant nunca é exercido em ambiente real. A consulta cruzada da outbox é uma excepção deliberada que depende da integridade do worker. A configuração de `tenant-id` no APIM é POR VALIDAR NO TENANT.
- **Owner:** por atribuir (papel sugerido: arquitecto de segurança).

### T-09 Operador comprometido

- **Fronteira:** F4 e F8.
- **Pré-condição:** uma conta de operador ou de administrador é controlada por um atacante.
- **Mitigação:** quatro papéis separados (Reader, IncidentOperator, PolicyAdmin, Auditor). Reactivar exige pedido de um PolicyAdmin e aprovação de OUTRO PolicyAdmin, com grants externos reverificados, nova `revocation_epoch` e nova `policy_version`. Cada acção de operador regista o iniciador humano. Suspender é rápido e exige só IncidentOperator, o que é falha segura: um operador comprometido pode causar indisponibilidade, não acesso. Autenticação forte e acesso privilegiado temporário "quando suportado e licenciado" (secção 13 da especificação).
- **Teste:** BT-19 (operador sem papel não suspende, reactiva nem concede capacidades), local. TC-09 (local): `Reactivation_needs_second_person_revalidated_grants_and_kills_old_capabilities` e `Activation_needs_a_second_person`.
- **Evidência:** `agent.reactivation_requested` e `agent.reactivated` com iniciadores humanos distintos, `agent.suspended` com iniciador.
- **Risco residual:** dois PolicyAdmin em conluio, ou comprometidos, reactivam. Um administrador de SQL ou de Azure fora da aplicação não é travado por ela. A ledger só dá evidência, não prevenção: "an attacker or system administrator who has control of the machine can bypass all system checks" [M39]. O acesso privilegiado temporário e a autenticação forte dependem de licenças por confirmar.
- **Owner:** por atribuir (papel sugerido: responsável de segurança da informação).

### T-10 Identidade partilhada

- **Fronteira:** F3 e F6.
- **Pré-condição:** vários agentes partilham o mesmo blueprint, a mesma managed identity user-assigned ou o mesmo executor, e a suspensão de um afecta ou deixa passar outro.
- **Mitigação:** cada agente tem o seu `ExecutorObjectId`. Grants no próprio agente contam como caminho directo e baixam a cobertura para `DirectAccessPossible`. A desactivação por omissão é do agente individual. Desactivar um blueprint exige âmbito visível, impacto e aprovação distintos. Não se reutiliza a mesma managed identity entre runtimes com âmbitos incompatíveis.
- **Teste:** BT-13 (desactivar um agente não desactiva irmãos do blueprint), só em simulação. BT-06 só nas fases C e D. TC-10 NÃO implementado: o código só recusa um executor igual à identidade do agente (`Agent_without_owner_or_with_shared_executor_identity_cannot_be_registered`). Dois agentes diferentes com o mesmo `ExecutorObjectId` não são recusados.
- **Evidência:** `coverage.changed`, `alert.direct_access_discovered`, `containment.step` por agente.
- **Risco residual:** as credenciais vivem no blueprint e são partilhadas por todas as identidades filhas: "Treat each blueprint as a credential boundary" [M14]. Desactivar o blueprint bloqueia as identidades existentes e impede novas [M06]. O efeito real da desactivação de uma agent identity sobre irmãos, e sobre o agent user associado, só se valida no tenant na fase D. O BT-13 local prova só que o adaptador actua num objecto. A cascata para o agent user é NÃO VERIFICADA [M21].
- **Owner:** por atribuir (papel sugerido: arquitecto de identidade).

### T-11 Grant cumulativo

- **Fronteira:** F3 e F6.
- **Pré-condição:** o agente tem, por outra via, uma permissão mais ampla que a pretendida, e as autoridades somam-se.
- **Mitigação:** o acesso efectivo permitido pelo produto é a intersecção das restrições do produto com a autoridade externa. A `ReconciliationService` compara grants observados (`IExternalGrantSource`) com os registados, e um grant amplo (por exemplo `Sites.Read.All` ou `Files.Read.All`) é drift perigoso e suspende o agente. Em SharePoint, a revogação de um scope de nível superior não remove o acesso dado por um scope inferior ("the application maintains access to the list it was given specific access to") [M22], por isso a reconciliação lista todos os níveis. Em Exchange (fase posterior, fora do MVP), as permissões Entra e as do RBAC for Applications são uma união: "the union of an unscoped `Mail.Read` grant from Microsoft Entra and a resource-scoped `Mail.Read` grant in Application RBAC results in no effective resource scoping" [M32]. O ledger tem de verificar as duas autoridades.
- **Teste:** BT-15 (drift de grants detectado e tratado), local. TC-11 a criar (fase D, só quando o correio entrar): permissão `Mail.Read` consentida no Entra anula o âmbito do RBAC, e `Test-ServicePrincipalAuthorization` não o mostra porque exclui permissões do Entra [M32].
- **Evidência:** `alert.broad_grant`, `alert.drift_detected`, `grant.recorded`, `grant.absent`.
- **Risco residual:** grants concedidos entre ciclos de reconciliação funcionam até serem detectados. O intervalo ainda não está definido. Listar e obter permissões num site por Graph é NÃO VERIFICADO nas páginas lidas, e a reconciliação precisa disso. Em Exchange, uma alteração de papel leva 30 minutos a 2 horas a propagar, e o cmdlet de teste ignora essa cache [M32].
- **Owner:** por atribuir (papel sugerido: administrador do tenant, com o arquitecto de segurança).

### T-12 Drift

- **Fronteira:** F3 e F6.
- **Pré-condição:** o estado real do Entra ou do recurso muda fora do produto (consentimento novo, grant novo, responsável removido), e os eventos perdem-se ou atrasam-se.
- **Mitigação:** reconciliação periódica obrigatória, para além de eventos. Cada `ExternalGrant` guarda `LastVerifiedAt`, origem e `LastResult`. O estado do agente e a cobertura declaram a última verificação (`Coverage.LastVerifiedAt`). Drift perigoso suspende o agente ou impede nova activação. Alertas mínimos: acesso directo descoberto, drift, grant amplo e conta sem responsável.
- **Teste:** BT-15 (drift de grants detectado e tratado), local.
- **Evidência:** `alert.drift_detected`, `alert.broad_grant`, `alert.direct_access_discovered`, `grant.recorded`.
- **Risco residual:** a reconciliação precisa de permissões de leitura no Entra e no recurso, e essa matriz é POR PREENCHER NO TENANT. Uma leitura incompleta esconde grants. O intervalo é uma decisão em aberto. Eventos do Entra não são autoridade. O Agent 365 e o centro de administração Microsoft 365 têm um segundo interruptor de bloqueio, e se o bloqueio lá altera o Entra é NÃO VERIFICADO [M14] [M64].
- **Owner:** por atribuir (papel sugerido: engenheiro de plataforma, com o administrador Entra).

### T-13 Revogação concorrente

- **Fronteira:** F2, F5 e F3.
- **Pré-condição:** uma suspensão chega enquanto uma acção já foi admitida ou está a caminho do Graph.
- **Mitigação:** a admissão lê o agente pelo caminho com bloqueio (`GetAgentAsync` segura o registo até ao fim da transacção), por isso admissão e suspensão são serializadas. A transacção 2 revalida estado, epoch e capacidade e só então marca `Executing`. A suspensão é uma transacção única: muda o estado, incrementa `revocation_epoch`, cria a `ContainmentOperation` em `LocalBlocked`, regista `InFlightActionIds`, escreve outbox e auditoria. `Agent.Version` dá controlo optimista de concorrência. Acções já em `Executing` não se podem parar e são reportadas como em curso, separadamente.
- **Teste:** BT-04 (corrida suspensão e execução documenta exactamente as acções em curso) e BT-03 (suspensão invalida capacidade anterior ainda válida), locais.
- **Evidência:** `InFlightActionIds` na operação de contenção, `action.cancelled_at_revalidation`, `action.executing`, `agent.suspended`.
- **Risco residual:** a revalidação não elimina a corrida com um pedido já enviado ao Graph. Não existe rollback universal. No MVP só há leitura, e os bytes podem já ter sido entregues quando a suspensão é persistida. O SLO de 5 segundos para recusar novas admissões é objectivo inicial, por medir na fase C.
- **Owner:** por atribuir (papel sugerido: engenheiro de plataforma responsável pelo núcleo).

### T-14 Mensagens fora de ordem

- **Fronteira:** F5 e F6.
- **Pré-condição:** uma mensagem da outbox chega duplicada, tarde ou depois de uma reactivação, por exemplo uma ordem de contenção antiga.
- **Mitigação:** cada mensagem leva a `Epoch`. Epoch diferente da actual significa descartada como `superseded` (`containment.superseded`), nunca executada. O worker nunca reactiva um agente. Os consumidores revalidam antes de executar e depois de retentativas. Mensagens antigas não reactivam carteiras. A ordem não é garantida pelo transporte. FIFO exige sessões no Service Bus [M56], e a disponibilidade de sessões por escalão é NÃO VERIFICADA.
- **Teste:** TC-14 (local): `Old_containment_message_never_acts_on_a_reactivated_agent_and_never_reactivates`: uma mensagem de epoch anterior, entregue depois da reactivação, é descartada e auditada, e não altera o estado. BT-03 e BT-10 são relacionados.
- **Evidência:** `containment.superseded`, `containment.dead_lettered`.
- **Risco residual:** passos de adaptadores de uma mesma epoch podem chegar fora de ordem, e dependem de serem idempotentes. A fila de dead-letter não se limpa sozinha ("there's no automatic cleanup of the DLQ") [M52], logo precisa de processo operacional.
- **Owner:** por atribuir (papel sugerido: engenheiro de plataforma responsável do ContainmentWorker).

### T-15 Retry duplicado

- **Fronteira:** F1, F5 e F6.
- **Pré-condição:** o cliente repete `POST /actions`, o Service Bus reentrega uma mensagem, ou o worker repete um passo após throttling.
- **Mitigação:** chave de idempotência obrigatória, por agente. Mesma chave e argumentos: resultado anterior, sem repetir efeito. Argumentos diferentes: `idempotency_conflict`. Acção ainda a decorrer: `in_progress`. Os adaptadores de contenção são idempotentes, com timeout, retentativas limitadas e backoff, e respeitam `Retry-After` [M30] [M31]. O Service Bus em modo peek-lock dá entrega pelo menos uma vez, e "if your scenario can't tolerate duplicate processing, add extra logic" [M50], por isso o consumidor é idempotente. A ledger não permite corrigir uma linha duplicada (inferência de [M42]).
- **Teste:** BT-10 (repetição de mensagem não duplica efeito), local.
- **Evidência:** `idempotent_replay`, `in_progress`, `Attempt`, `containment.step` com tentativas.
- **Risco residual:** não existe exactly-once universal para efeitos Graph. A leitura é idempotente por natureza. As fases com escrita (rascunhos de correio) exigem registo de intenção, reconciliação e, quando necessário, confirmação humana. A detecção de duplicados do Service Bus só actua no envio, dentro da janela (10 minutos por omissão) [M51], e não cobre reentrega ao consumidor.
- **Owner:** por atribuir (papel sugerido: engenheiro de plataforma).

### T-16 Indisponibilidade de SQL

- **Fronteira:** F5.
- **Pré-condição:** o SQL não responde, ou não consegue persistir a intenção ou o resultado.
- **Mitigação:** falha fechada. Uma `StoreUnavailableException` na admissão dá `denied` com `state_unavailable`. Não existe cache positiva. A intenção tem de ficar persistida antes de qualquer efeito externo. Se a revalidação não puder ler o estado, a acção fica `Admitted` sem efeito e a varredura (`SweepStaleActionsAsync`) fecha-a. Se o resultado não puder ser gravado, o executor não entrega os dados (`result_not_recorded`) e a acção fica para a varredura, que a marca `Unknown`. A indisponibilidade de telemetria secundária não pode causar perda silenciosa do registo primário.
- **Teste:** BT-08 (SQL indisponível: negar novas acções protegidas), local. BT-17 (restauro e failover não reactivam um agente suspenso) só nas fases C e D.
- **Evidência:** resposta `state_unavailable` do executor, registada na telemetria operacional (secundária: durante a indisponibilidade não há registo na ledger). Alerta de disponibilidade.
- **Risco residual:** a disponibilidade do SQL passa a ser a disponibilidade do produto, por escolha. Um restauro pode repor um estado anterior em que o agente estava activo, por isso o restauro exige validar RPO e reconciliar antes de reabrir tráfego (BT-17). Os digests só cobrem dados replicados em secundários geográficos, por desenho da Microsoft [M41]. Uma acção cujo efeito ocorreu e cujo resultado não foi gravado fica `Unknown`.
- **Owner:** por atribuir (papel sugerido: DBA ou engenheiro de plataforma de dados).

### T-17 Indisponibilidade de APIM

- **Fronteira:** F1 e F2.
- **Pré-condição:** o APIM está em baixo ou mal configurado.
- **Mitigação:** o APIM é o ponto de entrada, não a autoridade. O executor valida o token e lê o estado autoritativo por si [M38]. Com o APIM em baixo o agente não chega ao executor, porque o executor não tem exposição directa (ingresso interno, a definir no Bicep da fase C). Não existe caminho de contingência que desvie o gateway.
- **Teste:** TC-17 a criar (fase C): derrubar o APIM faz os pedidos falharem, e uma chamada directa ao executor sem credencial do APIM é recusada. BT-05 (fase C): gateway e backend não podem ser contornados.
- **Evidência:** alertas de disponibilidade do gateway, pedidos falhados no APIM, recusas no executor.
- **Risco residual:** os mecanismos de rede aplicam-se ao troço APIM para executor [M55], não ao Graph (inferência). A disponibilidade por escalão do APIM é NÃO VERIFICADA. Quem edita políticas APIM pode autenticar-se como a managed identity do serviço e extrair o token, e o APIM "does not validate which backend the token is sent to" [M37]. Por isso a credencial Graph fica no executor e não no APIM (inferência).
- **Owner:** por atribuir (papel sugerido: responsável da plataforma de integração, APIM).

### T-18 Indisponibilidade de Entra

- **Fronteira:** F1, F4 e F6.
- **Pré-condição:** o Entra não emite tokens, não valida, ou não aceita as chamadas dos adaptadores de contenção.
- **Mitigação:** o bloqueio local (`LocalBlocked`) não depende do Entra e continua eficaz. Os adaptadores têm timeout, retentativas limitadas, backoff e dead-letter. O resultado fica visível por passo (`Failed` ou `Unknown`) e o estado global não passa a `VerifiedScoped`: qualquer `Unknown` dá `Unknown`, mistura dá `Partial`, tudo falhado dá `Failed`. A validação de assinatura usa cache de chaves: na política `validate-jwt` as chaves são obtidas a cada hora e guardadas, com leitura no máximo de 5 em 5 minutos [M36]. Para `validate-azure-ad-token` esse comportamento é NÃO VERIFICADO. O acesso de operadores em falha do Entra depende do break-glass independente da aplicação.
- **Teste:** TC-18 (local, com falhas injectadas na simulação): `Repeated_failure_is_bounded_and_reported_as_failed` e `Suspended_never_becomes_verified_without_external_steps`. Timeout, throttling e erro nos adaptadores produzem `Unknown`, `Partial` ou `Failed`, nunca `VerifiedScoped`. BT-11 e BT-12 são relacionados. BT-20 (break-glass com a aplicação indisponível) só na fase D.
- **Evidência:** `containment.step`, `containment.completed` com o estado global, `containment.dead_lettered`, campo de incertezas da `ContainmentOperation`.
- **Risco residual:** sem Entra a contenção externa não avança. O bloqueio local só cobre o caminho pelo gateway. Um agente com grant directo mantém o acesso. Os tokens já emitidos mantêm-se (ver T-02).
- **Owner:** por atribuir (papel sugerido: arquitecto de identidade, com o responsável de continuidade).

### T-19 Falha de digests

- **Fronteira:** F7.
- **Pré-condição:** os digests deixam de ser exportados, vão para um destino adulterável, ou a verificação não corre.
- **Mitigação:** gestão nativa de digests do SQL Ledger para armazenamento independente. Os digests são gerados num intervalo de 30 segundos, e só se houver transacções [M41]. Baseline: Blob com política de imutabilidade validada e `locked` depois de aprovada a retenção [M46]. Privilégios SQL, destino dos digests e verificação separados. Monitorização da idade do último digest, falhas de exportação, resultado da verificação e cobertura temporal. A Microsoft recomenda alertas sobre uploads falhados e sobre a queda de uploads com sucesso [M45]. Verificação após restauro e failover.
- **Teste:** BT-16 (alteração de evidência detectada nos cenários suportados pela ledger). TC-19 a criar (fase C): revogar a permissão de escrita no armazenamento dispara o alerta, e apontar o upload para um destino sem política `locked` é detectado pela verificação de configuração.
- **Evidência:** alertas de digest, código de retorno da verificação (`0` sucesso, `1` falha) e `last_verified_block_id` [M40].
- **Risco residual:** a ledger dá evidência de adulteração, não prevenção, e não impede uma entrada inicial falsa nem garante disponibilidade nem recupera dados [M39]. "The verification process and the integrity of the database depend on the integrity of the input digests" [M40]. Com autenticação Entra-only, Elastic Jobs não é suportado, por isso o agendamento da verificação usa Azure Automation ou um job próprio [M40] [M53]. LRS não é suportado para digests automáticos [M42]. Os nomes das métricas de upload são NÃO VERIFICADOS. Azure Confidential Ledger como destino tem garantia de integridade mais forte contra administradores privilegiados [M41], com limites próprios [M49], e é opção por decidir.
- **Owner:** por atribuir (papel sugerido: DBA com o responsável de segurança, e o auditor para a verificação).

### T-20 Exfiltração

- **Fronteira:** F1, F3 e F7.
- **Pré-condição:** dados acessíveis ao agente saem por um caminho não previsto: o próprio agente, um URL pré-autenticado, logs e ledger, ou acesso directo ao recurso.
- **Mitigação:** mínimo privilégio por recurso (Selected, papel `read`) e duas operações de leitura. Limite por hora. A ledger guarda um digest SHA-256 dos argumentos e nunca tokens, conteúdo, prompts nem dados pessoais desnecessários. O executor devolve bytes e não o `Location`. No perfil de controlo forte removem-se os acessos directos do agente ao recurso, e o egress restrito é defesa adicional, não substituto. Defender, Sentinel e Purview são para avaliar e não se presume cobertura.
- **Teste:** BT-18 (sem segredos ou conteúdo sensível desnecessário na ledger e nos logs), local. BT-06 só nas fases C e D. TC-20 a criar (fase C): o runtime do agente não tem rota para Graph nem para o armazenamento.
- **Evidência:** `action.completed` com digest e referência mínima, ausência de conteúdo nos registos, `alert.direct_access_discovered`.
- **Risco residual:** o que o agente pode ler pode ser enviado por ele para fora. Eliminar dados já exfiltrados está fora do MVP. O URL pré-autenticado é uma credencial ao portador, sem exigir token, com validade documentada entre "minutes" [M25] e "1 hour" [M28], e a remoção de permissões pode não o invalidar de imediato [M28]. A cobertura de Defender e Purview é POR VALIDAR NO TENANT.
- **Owner:** por atribuir (papel sugerido: responsável de protecção de dados, com o responsável de segurança).

### T-21 Operação externa com resultado desconhecido

- **Fronteira:** F3 e F6.
- **Pré-condição:** o pedido ao Graph, ou um passo de contenção, termina sem confirmação (timeout, erro de rede, resposta não interpretável).
- **Mitigação:** a chamada externa tem timeout (`ExecutionOptions.ExternalTimeout`, 10 segundos por omissão no código, a calibrar na fase C). Um timeout dá `ActionState.Unknown` com `external_timeout`, e um erro não classificado dá `Unknown` com `external_error_unknown`, nunca sucesso inventado. Throttling dá `Failed` com `external_throttled`, e o executor respeita `Retry-After`: o Graph devolve 429 [M30] e o SharePoint 429 ou 503 [M31], e pedidos limitados dentro de um batch não são repetidos automaticamente [M30]. Na contenção, qualquer passo `Unknown` torna o estado global `Unknown`. A varredura fecha acções sem resultado como `Unknown`. A reconciliação e a confirmação humana tratam o que ficou por saber.
- **Teste:** BT-11 (timeout externo produz unknown e reconciliação), BT-12 (throttling gera estado parcial visível) e BT-04, locais, sobre falhas injectadas no mundo simulado.
- **Evidência:** `action.completed` com `execution_state` `Unknown`, `containment.step`, lista de incertezas da operação de contenção.
- **Risco residual:** nunca se sabe se uma leitura com timeout foi servida. Para efeitos de escrita (fases posteriores) é preciso reconciliar com a fonte. Não há exactly-once universal, nem rollback universal. Os limites reais do SharePoint dependem do número de licenças, e testes de carga não são permitidos em SharePoint Online [M31], o que limita a fase E.
- **Owner:** por atribuir (papel sugerido: engenheiro de plataforma, com o operador de incidentes).

## 4. Resumo: ameaça, teste e estado

Critério do estado, aplicado à mitigação principal de cada ameaça:

- **coberto localmente:** a mitigação principal tem BT ou TC com prova local, sobre o mundo simulado. Parte pode ficar por provar ou por implementar, indicada na linha.
- **só fases C/D:** a mitigação principal só se prova com recursos reais, nas fases C ou D.
- **por criar:** a especificação não tem BT para a mitigação principal e o teste `TC` só se pode fazer na fase C.

Uma classificação "coberto localmente" não é prova de contenção real: o mundo Microsoft é simulado e uma resposta 200 ou uma simulação não provam contenção (secção 18 da especificação).

| Ameaça | BT | Testes a criar | Estado |
|---|---|---|---|
| T-01 Prompt injection | BT-01 | TC-01 (local) | coberto localmente |
| T-02 Roubo de token | BT-02 (local), BT-06 e BT-14 (C/D) | TC-02 (fase C) | só fases C/D |
| T-03 Replay | BT-10, BT-03 | TC-03 (fase C) | coberto localmente |
| T-04 Confused deputy | BT-02, BT-07 | TC-04 (local) | coberto localmente |
| T-05 SSRF | BT-05 (C/D, complementar) | TC-05 (local e fase C) | coberto localmente (parcial: ligação de saída só na fase C) |
| T-06 Batch e redirect bypass | BT-05 (C/D, complementar) | TC-06 (local) | coberto localmente (parcial: `$expand`, `$select` e `Location` sem teste dedicado) |
| T-07 Recursos canónicos manipulados | BT-01 (complementar) | TC-07 (local) | coberto localmente |
| T-08 Escalada entre tenants | BT-02 | TC-08 (local) | coberto localmente |
| T-09 Operador comprometido | BT-19 | TC-09 (local) | coberto localmente |
| T-10 Identidade partilhada | BT-13 (só simulação), BT-06 (só simulação) | TC-10 (não implementado) | coberto localmente (parcial: BT-13 e BT-06 só em simulação, efeito real na fase D; TC-10 por implementar) |
| T-11 Grant cumulativo | BT-15 | TC-11 (fase D, correio) | coberto localmente (SharePoint; Exchange fora do MVP) |
| T-12 Drift | BT-15 | nenhum | coberto localmente |
| T-13 Revogação concorrente | BT-04, BT-03 | nenhum | coberto localmente |
| T-14 Mensagens fora de ordem | BT-03, BT-10 (relacionados) | TC-14 (local) | coberto localmente |
| T-15 Retry duplicado | BT-10 | nenhum | coberto localmente |
| T-16 Indisponibilidade de SQL | BT-08 (local), BT-17 (C/D) | nenhum | coberto localmente (restauro só nas fases C/D) |
| T-17 Indisponibilidade de APIM | BT-05 (C/D) | TC-17 (fase C) | por criar |
| T-18 Indisponibilidade de Entra | BT-11, BT-12 (relacionados), BT-20 (C/D) | TC-18 (local) | coberto localmente (parcial: BT-20 só na fase D) |
| T-19 Falha de digests | BT-16 (complementar) | TC-19 (fase C) | por criar |
| T-20 Exfiltração | BT-18 (local), BT-06 (C/D) | TC-20 (fase C) | coberto localmente (parcial: caminhos directos só nas fases C/D) |
| T-21 Resultado desconhecido | BT-11, BT-12, BT-04 | nenhum | coberto localmente |

Contagem: 18 coberto localmente, 1 só fases C/D, 2 por criar, num total de 21 ameaças. Entre os 18, T-05, T-06, T-10, T-11, T-16, T-18 e T-20 têm parte por provar ou por implementar, indicada na linha.

## 5. Testes bloqueantes da especificação (secção 15)

| Id | Teste | Prova |
|---|---|---|
| BT-01 | Recurso autorizado funciona, não autorizado falha | local |
| BT-02 | Chamador e tenant falsificados falham | local |
| BT-03 | Suspensão invalida capacidade anterior ainda dentro da validade | local |
| BT-04 | Corrida suspensão e execução documenta exactamente as acções em curso | local |
| BT-05 | Gateway e backend não podem ser contornados | só fases C/D |
| BT-06 | O agente não tem credencial alternativa para Graph | só simulação (real: fases C/D) |
| BT-07 | Retirar uma capacidade não altera outras indevidamente | local |
| BT-08 | SQL indisponível: negar novas acções protegidas | local, SQL real |
| BT-09 | Bus indisponível: o bloqueio persistente continua eficaz | local |
| BT-10 | Repetição de mensagem não duplica efeito indevidamente | local, SQL real |
| BT-11 | Timeout externo produz unknown e reconciliação, não sucesso inventado | local |
| BT-12 | Throttling Microsoft gera estado parcial visível | local |
| BT-13 | Desactivar um agente não desactiva irmãos do blueprint | só simulação (real: fase D) |
| BT-14 | Testar token previamente emitido num recurso de teste e reportar o resultado real | só simulação (real: fases C/D) |
| BT-15 | Drift de grants é detectado e tratado | local |
| BT-16 | Alteração de evidência é detectada nos cenários suportados pela ledger | local, SQL real |
| BT-17 | Restauro e failover não reactivam um agente suspenso, com RPO e reconciliação validados | só fases C/D |
| BT-18 | Sem segredos ou conteúdo sensível desnecessário na ledger e nos logs | local |
| BT-19 | Operador sem papel não consegue suspender, reactivar ou conceder capacidades | local |
| BT-20 | Break-glass funciona com a aplicação indisponível | só fases C/D |

BT-16 local corre contra um SQL Server 2022 num contentor (`AP_TEST_SQL`, ver `test-plan.md`). A verificação contra digests guardados em armazenamento imutável independente só existe na fase C.

## 6. Testes a criar (TC)

| Id | Ameaça | Descrição | Onde corre |
|---|---|---|---|
| TC-01 | T-01 | Argumentos com URL, `..`, parâmetros extra ou texto de instruções são recusados sem chamar o gateway de dados | local, unitário |
| TC-02 | T-02 | Token do tenant com audiência errada ou de outra aplicação cliente: 401 no APIM e recusa no executor | fase C |
| TC-03 | T-03 | Token capturado antes da suspensão e reutilizado depois recebe `agent_suspended` através do APIM | fase C |
| TC-04 | T-04 | Agente A pede recurso do agente B com a sua capacidade e recebe `capability_missing` | local |
| TC-05 | T-05 | Argumentos tipo URL, IP, esquema ou anfitrião recusados. Na fase C, o executor não liga a destinos fora da lista | local e fase C |
| TC-06 | T-06 | `$batch`, `$expand`, `$select` recusados. A resposta nunca contém `Location` nem `downloadUrl` | local, unitário |
| TC-07 | T-07 | Tabela de variantes de grafia do recurso, todas com `resource_not_canonical` | local, unitário |
| TC-08 | T-08 | `agentId` de outro tenant devolve resposta igual à de agente inexistente | local |
| TC-09 | T-09 | O mesmo PolicyAdmin não pede e aprova a reactivação | local |
| TC-10 | T-10 | Dois agentes com o mesmo `ExecutorObjectId` são recusados | local |
| TC-11 | T-11 | Permissão `Mail.Read` no Entra anula o âmbito do RBAC for Applications | fase D, quando o correio entrar |
| TC-14 | T-14 | Mensagem de epoch anterior entregue depois da reactivação é descartada e auditada | local |
| TC-17 | T-17 | APIM indisponível faz os pedidos falhar. Chamada directa ao executor recusada | fase C |
| TC-18 | T-18 | Falhas injectadas nos adaptadores nunca produzem `VerifiedScoped` | local |
| TC-19 | T-19 | Revogar escrita no armazenamento dispara o alerta de digest. Destino sem política `locked` é detectado | fase C |
| TC-20 | T-20 | O runtime do agente não tem rota para Graph nem para o armazenamento | fase C |

## 7. Lacunas deste documento

- Não foi validado nada em tenant. Todos os claims, formatos de identificadores e comportamentos de revogação marcados "POR VALIDAR NO TENANT" mantêm-se por validar.
- O estado dos BT locais segue o desenho. Confirmar contra a suite quando estiver estável.
- A lista de lacunas da documentação Microsoft, com impacto, está em `gaps-and-assumptions.md`. O registo de riscos está em `risks.md`.

## 8. Fontes

Todas verificadas em 2026-11-11. Prefixo: `https://learn.microsoft.com/en-us/`. A coluna "Leitura" indica se a página foi lida por inteiro ou só por excerto de pesquisa. O que vem de excerto não pode sustentar, sozinho, uma decisão.

| Chave | Caminho | Leitura |
|---|---|---|
| M01 | `entra/identity/users/users-revoke-access` | inteira |
| M02 | `entra/identity/conditional-access/concept-continuous-access-evaluation` | inteira |
| M03 | `entra/identity/conditional-access/concept-continuous-access-evaluation-workload` | inteira |
| M04 | `entra/identity/conditional-access/agent-id` | inteira |
| M06 | `entra/agent-id/manage-agent-identities-admin` | inteira |
| M07 | `entra/identity-platform/configurable-token-lifetimes` | inteira |
| M08 | `entra/identity-platform/access-tokens` | inteira |
| M09 | `entra/identity/managed-identities-azure-resources/managed-identities-faq` | inteira |
| M14 | `microsoft-agent-365/developer/identity` | inteira |
| M21 | `entra/agent-id/agent-users` | inteira |
| M22 | `graph/permissions-selected-overview` | inteira |
| M23 | `graph/api/site-post-permissions?view=graph-rest-1.0` | inteira |
| M25 | `graph/api/driveitem-get-content?view=graph-rest-1.0` | inteira |
| M26 | `graph/api/driveitem-get?view=graph-rest-1.0` | inteira |
| M27 | `graph/api/driveitem-list-children?view=graph-rest-1.0` | inteira |
| M28 | `graph/api/resources/driveitem?view=graph-rest-1.0#instance-attributes` | excerto |
| M29 | `graph/json-batching` | inteira |
| M30 | `graph/throttling` | inteira |
| M31 | `sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online` | inteira |
| M32 | `exchange/permissions-exo/application-rbac` | inteira |
| M35 | `azure/api-management/validate-azure-ad-token-policy` | inteira |
| M36 | `azure/api-management/validate-jwt-policy` | excerto |
| M37 | `azure/api-management/api-management-howto-use-managed-service-identity#security-considerations-for-managed-identities` | excerto |
| M38 | `entra/msidweb/advanced/api-gateways#follow-best-practices` | excerto |
| M39 | `sql/relational-databases/security/ledger/ledger-overview` | inteira |
| M40 | `sql/relational-databases/security/ledger/ledger-database-verification` | inteira |
| M41 | `sql/relational-databases/security/ledger/ledger-digest-management` | inteira |
| M42 | `sql/relational-databases/security/ledger/ledger-limits` | inteira |
| M45 | `sql/relational-databases/security/ledger/ledger-monitor-digest-uploads` | inteira |
| M46 | `azure/storage/blobs/immutable-storage-overview` | inteira |
| M49 | `azure/confidential-ledger/limits` | inteira |
| M50 | `azure/service-bus-messaging/service-bus-message-loss-and-duplicates` | excerto |
| M51 | `azure/service-bus-messaging/duplicate-detection` | inteira |
| M52 | `azure/service-bus-messaging/service-bus-dead-letter-queues` | inteira |
| M53 | `azure/azure-sql/database/authentication-azure-ad-only-authentication` | inteira |
| M55 | `azure/api-management/secure-api-management#network-security` | excerto |
| M56 | `azure/service-bus-messaging/message-sessions` | excerto |
| M64 | `microsoft-agent-365/admin/manage-agents` | excerto |
