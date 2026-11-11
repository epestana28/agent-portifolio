# Matriz de cobertura de contenção

Estado: rascunho por validar no tenant

Verificado em documentação oficial a 2026-11-11

## 1. Finalidade e estado real

Este documento classifica, por tipo de agente ou identidade, o que o produto consegue conter e com que prova. Responde à secção 20 da especificação ("classificação de cobertura por tipo de agente").

As classificações abaixo são tectos. Indicam o máximo que o produto pode atribuir honestamente se as condições escritas forem cumpridas e provadas. Enquanto não forem provadas, a classificação por omissão é `Unverified` e o estado de contenção por omissão é `unknown`.

Estado do projecto, sem exagero:

- Bloqueio local: implementado na fase B, em .NET 8, sobre um armazém local. O comportamento está provado só em testes locais.
- Contenção externa: os adaptadores de Entra, remoção de grants, isolamento de runtime e sonda de token antigo são simulados, com injecção de falhas. Não existe adaptador real para Entra nem para Graph.
- Fases C, D e E: não executadas. Os testes BT-05, BT-06, BT-14, BT-17 e BT-20 só ficam provados nas fases C e D. Localmente existe, no máximo, uma aproximação simulada que não conta como prova.
- Nada neste documento foi medido em tenant. Todos os tempos citados vêm da documentação.

Legenda: Verificado (texto lido na página), Inferência (conclusão a partir de textos verificados), NÃO VERIFICADO (a documentação lida não o afirma). Todas as fontes são páginas de Microsoft Learn verificadas a 2026-11-11, com prefixo `https://learn.microsoft.com/en-us/` quando abreviadas. Os códigos Lxx remetem para `permission-matrix.md`.

## 2. Definições

### Classificação de cobertura

| Classificação | Significado | Condição para a atribuir |
| --- | --- | --- |
| `GatewayEnforced` | Toda a acção de dados do agente, no âmbito declarado, passa pelo gateway e pelo executor. | Inventário de caminhos alternativos feito e vazio, para o âmbito declarado. O agente não tem grant próprio nem credencial própria para o recurso. Teste negativo de acesso directo executado. `direct_access_verified_absent` verdadeiro, com `last_verified_at` recente. |
| `DirectAccessPossible` | Existe, ou não se consegue excluir, um caminho directo ao recurso. | Basta um grant no próprio agente, uma credencial partilhada, ou um acesso local ao recurso. Grants no próprio agente contam como caminho directo. |
| `Unverified` | Ainda não há inventário nem teste suficientes. | É o valor por omissão. |

A classificação aplica-se a um âmbito (recursos e operações) e nunca é universal.

### Estado de contenção

Estados globais: `requested`, `local_blocked`, `external_pending`, `verified_scoped`, `partial`, `failed`, `unknown`. Estados por passo: Requested, Executed, Verified, Failed, Unknown.

- `verified_scoped` exige que todos os passos sejam verificados por sonda e que o âmbito seja explícito. Um passo "token anterior recusado" só é Verified se a sonda mostrar a recusa. Se a sonda mostrar que o token foi aceite durante um tempo medido, o passo é Failed, o estado global é `partial` e o tempo medido fica visível.
- Qualquer passo Unknown dá `unknown`. A mistura de passos verificados e não verificados dá `partial`. Tudo falhado dá `failed`.
- `suspended` nunca passa sozinho a `verified_scoped`. A confirmação administrativa (a API respondeu) não é confirmação por sonda.

### O bloqueio local é comum a todos os tipos

Em todos os tipos, o bloqueio local é o mesmo e é a única parte que o produto controla sozinho. A suspensão persiste, numa transacção, o estado `Suspended`, o novo `revocation_epoch`, a operação de contenção em `LocalBlocked` e a mensagem de outbox. Depois disso, o executor recusa novas admissões. A especificação fixa como objectivo recusar novas admissões até 5 segundos após a confirmação persistida da suspensão no ambiente piloto. É um objectivo, ainda sem medição real.

O que o bloqueio local não faz: não invalida tokens já emitidos pela Microsoft, não cancela uma chamada já enviada ao Graph, e não tira ao agente grants que ele tenha directamente no recurso. As acções já admitidas e em curso são listadas e reportadas à parte (`InFlightActionIds`).

## 3. Quadro resumo

| Tipo | Bloqueio local (gateway) | Acção externa documentada | Tokens já emitidos | CAE | Classificação máxima | Estado de contenção máximo |
| --- | --- | --- | --- | --- | --- | --- |
| Entra Agent ID | Sim, comum | Desactivar agent identity (v1.0). Desactivar agent user. Remover grants no recurso. Revogar sessões do agent user (inferência) | Destino NÃO VERIFICADO | NÃO VERIFICADO | `GatewayEnforced` se o inventário provar ausência de grants directos. Caso contrário `DirectAccessPossible` ou `Unverified` | `verified_scoped` só para o recurso testado e só se a sonda de token anterior for recusada. Senão `partial` |
| Service principal | Sim, comum | `accountEnabled` igual a false (v1.0). Remover grants no recurso. Rodar credenciais | Sem afirmação directa. Válidos até expirar, por analogia (inferência) | Sim, com restrições estreitas | `GatewayEnforced` se o inventário provar ausência de grants directos | `verified_scoped` só para Graph e para o recurso testado, com CAE `cp1` activo ou sonda favorável. Senão `partial` |
| Managed identity | Sim, comum | Sem disable documentado. Remover grants no recurso. Parar ou isolar o runtime | Cache de cerca de 24 horas, sem refresh forçado | Não | `GatewayEnforced` se a identidade não estiver partilhada e o inventário provar ausência de grants directos | `verified_scoped` só para o recurso em que a sonda confirme a recusa do token anterior. Para o resto `partial` |
| Copilot Studio | Só se as chamadas passarem pelo executor | Block no centro de administração Microsoft 365 (excerto). Se for aplicação clássica, aplicam-se as linhas de service principal | NÃO VERIFICADO | NÃO VERIFICADO | `Unverified` | `unknown`, ou `partial` num passo verificado |
| Foundry | Só se as chamadas passarem pelo executor | Como Copilot Studio. Pode ser aplicação clássica ou service principal | NÃO VERIFICADO | NÃO VERIFICADO | `Unverified` | `unknown`, ou `partial` num passo verificado |
| MCP | Só nas ferramentas servidas pelo executor | Depende do servidor MCP. Sem acção Entra se o servidor usar chave de API | Depende do servidor | Não aplicável | `Unverified` hoje. Tecto teórico `GatewayEnforced` limitado às ferramentas do executor, fora do MVP | `unknown`, ou `partial` |
| Terceiros | Só se as chamadas passarem pelo executor | Remover consentimento e grants no tenant. Políticas de workload identities não cobrem apps multitenant nem SaaS de terceiros | Fora do controlo do tenant | Fora de âmbito (documentado) | `Unverified` | `partial` no melhor caso, `unknown` por omissão |

## 4. Tipos dentro do MVP

### 4.1 Entra Agent ID

Verificado. O Entra Agent ID tem quatro objectos: blueprint, blueprint principal, agent identity e agent user. A agent identity é um service principal com `servicePrincipalType` igual a `ServiceIdentity`. As credenciais ficam no blueprint e são partilhadas por todas as identidades filhas: "Treat each blueprint as a credential boundary". Fonte: `https://learn.microsoft.com/en-us/microsoft-agent-365/developer/identity`.

Verificado. Há três modos de execução, cada um com um sujeito de token diferente: S2S (a agent identity é o sujeito), OBO (o utilizador é o sujeito e a agent identity é o actor) e Agentic-User (o agent user). Mesma fonte.

**O que se bloqueia localmente.** Novas admissões no executor, por estado e epoch. O executor tem identidade própria por agente (`ExecutorObjectId`) e o agente não recebe a credencial de dados. Se o agente não tiver grants próprios no recurso, o bloqueio local fecha o único caminho que o produto controla.

**Acção externa e evidência documental.**

- Desactivar a agent identity: `PATCH /servicePrincipals/{id}/microsoft.graph.agentIdentity`, v1.0, permissão `AgentIdentity.EnableDisable.All`, papel Agent ID Administrator. Efeito declarado: "block its access and token issuance". Fontes: `https://learn.microsoft.com/en-us/graph/api/agentidentity-update?view=graph-rest-1.0` e `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-identities-admin`. O corpo exacto do pedido é inferência (L04).
- Desactivar o agent user: propriedade `accountEnabled`, v1.0. Fonte: `https://learn.microsoft.com/en-us/graph/api/agentuser-update?view=graph-rest-1.0`. A desactivação da agent identity não está documentada como desactivando o agent user (L06).
- Remover grants Selected no recurso (G03, G04). Verificado: "Application access is lost as soon as a scope is revoked." Sem valor numérico.
- Revogar sessões do agent user: `revokeSignInSessions`, v1.0. A aplicação a agent users é inferência (L13).
- Remover atribuições Azure RBAC da agent identity, se existirem. Verificado: blueprints não podem ter papéis RBAC, atribuem-se à agent identity.
- Desactivar o blueprint principal desactiva todas as agent identities dele: "will no longer be able to authenticate". É um âmbito mais largo, com aprovação distinta. A desactivação individual é a acção por omissão (especificação, secção 8). Fonte: `https://learn.microsoft.com/en-us/entra/agent-id/manage-agent-blueprint`.
- Política Conditional Access tenant-wide: bloqueia emissão para categorias inteiras sem alterar objectos, em Graph beta e com requisito de licença contraditório entre páginas. Não é uma acção por agente. Fonte: `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities`.
- Block no centro de administração Microsoft 365 (excertos de pesquisa): segundo interruptor. Se é o mesmo estado que `accountEnabled`: NÃO VERIFICADO (L16).

**Tokens já emitidos.** NÃO VERIFICADO. Nenhuma página de Agent ID lida diz o que acontece aos access tokens já emitidos a uma agent identity. Toda a linguagem é sobre emissão futura. Prudência: assumir validade até à expiração. Ver secção 6.

**CAE.** NÃO VERIFICADO para agentes. A única menção é "Some resources also support Continuous Access Evaluation". Não se sabe que recursos, que eventos, nem se a desactivação de uma agent identity é um evento de revogação. Fonte: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id`. Inferência de risco: um cliente que declare `cp1` passa a receber tokens até 24 horas, e fora do Graph a revogação por CAE não existe.

**Limites do Conditional Access para agentes (verificados).** Não cobre o blueprint quando adquire token para criar identidades. Não cobre a troca intermédia de tokens no `AAD Token Exchange Endpoint: Public`. A política sobre a agent identity não cobre pedidos OBO nem o agent user. Não se aplica com Security defaults. Não se aplica a acesso por chave de API.

**Caminhos alternativos a inventariar.** Ver secção 7. Em particular: o fluxo OBO e o agent user são sujeitos de token distintos, e as credenciais do blueprint são partilhadas por identidades irmãs.

**Classificação máxima honesta.** `GatewayEnforced`, para o âmbito (site, biblioteca, operações) declarado, se: a agent identity não tem grant próprio no recurso; o executor tem identidade própria; o agent user, se existir, também não tem acesso; e o teste negativo de acesso directo passou. Se existir um grant amplo ou próprio, desce a `DirectAccessPossible`. Sem inventário, `Unverified`.

**Estado de contenção máximo.** `verified_scoped`, com este âmbito explícito: novas admissões recusadas no gateway, nova emissão de token pela agent identity recusada por sonda, grant do recurso R removido e leitura recusada por sonda, token anterior recusado em R por sonda (BT-14). O âmbito não inclui outros recursos que a agent identity alcance. Se a sonda de token anterior não for executada ou mostrar aceitação, o estado é `partial`, com o tempo medido. Se a desactivação não puder ser verificada, `unknown`.

### 4.2 Service principal

Verificado. Desactivação: `PATCH https://graph.microsoft.com/v1.0/servicePrincipals/{id}` com `{"accountEnabled": false}`, permissão `Application.ReadWrite.All`, papéis Cloud Application Administrator, Application Administrator, ou owner. Efeito declarado: impede a emissão de tokens "for that application", num só tenant. Fonte: `https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/disable-user-sign-in-portal`.

**O que se bloqueia localmente.** O mesmo bloqueio comum, no executor. Se o service principal for a identidade que o agente usa directamente para o Graph, o bloqueio local não o impede de falar com o Graph por fora do executor. Nesse caso a classificação desce.

**Acção externa e evidência documental.**

- `accountEnabled` igual a false (v1.0, verificado).
- Desactivar a application (`isDisabled`): âmbito global, em todos os tenants. A página usa beta para desactivar e v1.0 para reactivar. Verificado: "it immediately stops receiving new access tokens, but existing tokens remain valid until they expire". Fonte: `https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/deactivate-app-registration`.
- Remover grants Selected e consentimentos.
- Rodar credenciais, quando aplicável.
- Conditional Access para workload identities: só "Block access", só single-tenant, exige Workload Identities Premium, avaliada só quando o service principal pede token. Fonte: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity`.

**Tokens já emitidos.** A página de `accountEnabled` não o diz. Só afirma que impede a emissão. A única fonte que liga "service principal disable" a tokens existentes é a de CAE para workload identities. Inferência: fora das condições da CAE, os tokens mantêm-se válidos até expirar. Ver secção 6.

**CAE.** Aplicável, com restrições estreitas (verificadas). Só pedidos ao Microsoft Graph como recurso. Só service principals single-tenant de aplicações LOB. Só se o cliente declarar `cp1`. Eventos de revogação: "Service principal disable", "Service principal delete" e "High service principal risk". Aplicações multitenant e SaaS de terceiros estão fora de âmbito. Managed identities não são suportadas. Sem prazo numérico para service principals: a introdução diz "instantly", a página de utilizadores admite até 15 minutos. Fonte: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation-workload`.

**Classificação máxima honesta.** `GatewayEnforced` se o service principal que o agente usa não tem grant próprio no recurso e o executor usa outra identidade. `DirectAccessPossible` se o próprio service principal do agente tem grants amplos ou Selected.

**Estado de contenção máximo.** `verified_scoped` para Graph e para o recurso testado, se a sonda de token anterior for recusada (por CAE com `cp1`, ou por remoção do grant no recurso). Caso contrário `partial`, com o tempo medido. Para recursos que não sejam o Graph, a CAE não ajuda e fica `partial` ou `unknown`.

### 4.3 Managed identity

Verificado. Não existe, nas páginas lidas, procedimento Entra de desactivação equivalente a `accountEnabled` para managed identities. Se `accountEnabled` é suportado e eficaz no service principal de uma managed identity: NÃO VERIFICADO (L11).

**O que se bloqueia localmente.** O mesmo bloqueio comum. É o controlo principal neste tipo, porque os controlos Entra são os mais fracos dos três tipos.

**Acção externa e evidência documental.**

- Remover o grant no recurso: "Remove the identity's access to the target resource". Para Logic Apps (excerto): desactivar a identidade system-assigned ou remover a user-assigned. Fonte: `https://learn.microsoft.com/en-us/azure/logic-apps/authenticate-with-managed-identity`.
- Parar ou isolar o runtime a que a identidade está associada. Verificado: "The security boundary of the identity is the resource to which it's attached." Fonte: `https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq`.
- Eliminar a identidade não é acção de emergência (especificação, secção 8). Fica em soft delete durante 30 dias e os tokens anteriores continuam válidos.
- Conditional Access: "Managed identities aren't covered by policy." A página sugere access reviews como alternativa. Fonte: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity`.

**Tokens já emitidos (verificado).** Cache de tokens de cerca de 24 horas por URI de recurso. "Today, it isn't possible to force a managed identity's token to be refreshed before its expiry." Depois de eliminar a identidade, os tokens anteriores continuam válidos até à expiração original. Alguns sistemas, como o Azure RBAC, "will continue to accept requests from that token until it expires". Alterações de permissões podem demorar várias horas. Tempo de vida configurável não é suportado. Fonte: a mesma FAQ e `https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes`.

**CAE.** Não aplicável. "Managed identities aren't currently supported by CAE for workload identities."

**Aviso de desenho.** A documentação recomenda federar o blueprint a uma managed identity para não guardar segredo. O efeito da cache de 24 horas da managed identity sobre a revogação de um blueprint federado: NÃO VERIFICADO. A especificação proíbe reutilizar a mesma user-assigned managed identity entre runtimes com âmbitos incompatíveis. Uma identidade partilhada degrada a classificação.

**Classificação máxima honesta.** `GatewayEnforced` se a identidade não é partilhada e o inventário prova que não tem grants directos no recurso. Qualquer grant directo, ou partilha com outro runtime, dá `DirectAccessPossible`.

**Estado de contenção máximo.** Por omissão `partial`. Pode chegar a `verified_scoped` só para o recurso SharePoint em que a sonda confirme que o token anterior é recusado depois da remoção do grant (inferência, a medir em BT-14). Para qualquer outro recurso que a identidade alcance, `partial`, porque o Entra não oferece revogação e a validade pode chegar a 24 horas.

## 5. Casos fora do MVP

A especificação (secção 2) exclui do MVP a promessa de controlo de Copilot Studio, Foundry, MCP e terceiros. As linhas abaixo não são promessas. Servem para dizer qual é o tecto honesto e o que fica por inventariar.

### 5.1 Copilot Studio

- Verificado: agentes criados em versões anteriores do Copilot Studio podem existir como aplicações clássicas ou service principals e "don't have Microsoft Entra agent identities". Fonte: `https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities`. Nesse caso aplica-se a linha de service principal.
- Excertos de pesquisa (páginas não lidas por inteiro): bloquear um agente criado em SharePoint ou Foundry "only impacts its availability in Microsoft Copilot Chat". Bloquear uma instância "stops it and any actions it's performing". Fontes: `https://learn.microsoft.com/en-us/microsoft-agent-365/admin/agent-actions` e `https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-agent-instances`. Block no centro de administração não é, por si, revogação de acesso a dados.
- Local: o bloqueio no executor só vale se o agente chamar o executor. Um agente hospedado que use os seus conectores nativos não passa pelo gateway.
- CAE e tokens: NÃO VERIFICADO.
- Tecto: `Unverified`. Estado: `unknown`, ou `partial` se um passo for executado e verificado.

### 5.2 Foundry

- Aplica-se o mesmo texto de Copilot Studio sobre aplicações clássicas e Block.
- Verificado: o APIM AI gateway governa tráfego de modelos (limite de tokens, cache semântica, segurança de conteúdo, registo). "AI gateway in Microsoft Foundry" está em preview. Fonte: `https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities`. Inferência: não oferece autorização fina por ferramenta nem carteira por agente.
- Recursos Azure a que o runtime do Foundry acede podem ter Azure RBAC próprio, fora do controlo da carteira.
- Tecto: `Unverified`. Estado: `unknown`, ou `partial`.

### 5.3 MCP

- Verificado: o APIM suporta servidores MCP remotos e APIs A2A, e permite expor APIs REST como servidores MCP. Fonte: `https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities`.
- Local: o bloqueio vale para ferramentas que passem pelo gateway e pelo executor com a credencial do executor. Um servidor MCP remoto de terceiros, ou local ao runtime, fica fora.
- Acção externa: depende do servidor. Se usar chave de API, o Entra não é envolvido e Conditional Access não se aplica (verificado).
- Tecto hoje: `Unverified`. Tecto teórico, se vier a ser desenhado: `GatewayEnforced` limitado às ferramentas servidas pelo executor. Estado: `unknown`, ou `partial`.

### 5.4 Terceiros

- Verificado: Conditional Access para workload identities não cobre "Microsoft and third-party SaaS applications, including multitenant apps". CAE para workload identities: "Third-party SaaS and multitenant apps are out of scope". Fontes: `https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity` e a página de CAE para workload identities.
- Verificado: o acesso por chave de API contorna o pipeline Entra por completo.
- Acção externa possível no tenant: desactivar o service principal local (âmbito de um só tenant), remover consentimentos e grants. Não alcança tokens ou chaves emitidos fora do tenant.
- Tecto: `Unverified`. Estado: `partial` no melhor caso, `unknown` por omissão.

## 6. Tokens já emitidos: tempos documentados e lacunas

Todos os valores vêm da documentação e não foram medidos. As contradições entre páginas estão assinaladas. Usar o pior caso nas análises de risco.

| Item | Tempo documentado | Fonte | Lacuna |
| --- | --- | --- | --- |
| Access token por omissão (APIs registadas) | 60 a 90 minutos, média 75. Outras páginas dizem "1 hour". Sem Conditional Access, Teams e Microsoft 365 têm 2 horas | `https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes`, `https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens`, `https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access` | A página de `access-tokens` diz que o intervalo não se aplica a APIs da Microsoft, como o Graph. O valor para tokens do Graph não está garantido. Usar 90 minutos como pior caso sem CAE |
| Access token de agent identity | NÃO VERIFICADO | nenhuma | Nenhuma página específica de agentes |
| Tempo de vida configurável | Mínimo de 10 minutos, máximo de 23:59:59. Não suportado para managed identities. Não é respeitado em sessões CAE | `https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes` | Aplicabilidade a agent identities não verificada |
| Token CAE de utilizador | Até 28 horas (outras páginas: 24 a 28, 20 a 28) | `https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation` | Usar 28 horas como pior caso |
| Eventos críticos de CAE para utilizadores | "latency of up to 15 minutes might be observed" | mesma página | Não prometer revogação instantânea |
| Token CAE de workload identity | Até 24 horas | página de CAE para workload identities | Sem prazo numérico para a aplicação do evento de revogação |
| Revogação de sessões de utilizador | Atraso de "a few minutes" para os refresh tokens. Não invalida access tokens | `https://learn.microsoft.com/en-us/graph/api/user-revokesigninsessions?view=graph-rest-1.0` | Aplicação ao agent user é inferência |
| Desactivar application (`isDisabled`) | Tokens existentes válidos até expirar | `https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/deactivate-app-registration` | A mesma frase não está escrita para `accountEnabled` do service principal |
| Managed identity | Cache de cerca de 24 horas. Sem refresh forçado. Tokens válidos depois de eliminar a identidade | FAQ de managed identities | Efeito sobre blueprint federado não verificado |
| Alterações de permissões de managed identity | "several hours" | FAQ de managed identities | Sem valor máximo |
| URL de download pré-autenticado do SharePoint | "within minutes" numa página, "1 hour" noutra. Remover permissões "might not immediately invalidate the URL" | `https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0`, `https://learn.microsoft.com/en-us/graph/api/resources/driveitem?view=graph-rest-1.0` | Usar 1 hora como pior caso. Cada URL emitido é uma credencial ao portador |
| Revogação de grant Selected | "Application access is lost as soon as a scope is revoked." Sem valor numérico | `https://learn.microsoft.com/en-us/graph/permissions-selected-overview` | Tempo de propagação NÃO VERIFICADO. O efeito sobre um token já emitido que traz o scope é inferência (o recurso guarda o tuplo de aplicação, recurso e papel) |
| Cache de RBAC for Applications (Exchange, fase posterior) | 30 minutos a 2 horas, conforme a utilização | `https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac` | O cmdlet de teste ignora a cache |
| Sincronização de desprovisionamento de aplicações | Normalmente 20 a 40 minutos | `https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access` | Relevante só para aplicações com provisionamento |

Conclusão verificada para a camada de contenção: desactivar uma identidade e bloquear a emissão não invalida os access tokens já emitidos. A única via documentada para revogar access tokens é a CAE, e só nas condições restritas acima. Por isso a prova de contenção assenta em sondas com token anterior (BT-14), executadas num recurso de teste, com o resultado real reportado.

## 7. Caminhos alternativos a inventariar

Cada caminho encontrado reduz a classificação até ser eliminado ou contido. Este quadro é a lista de inventário. O estado de cada item no tenant está por preencher (ver `environment-assessment.md`).

| Caminho | Aplica-se a | Porque conta |
| --- | --- | --- |
| Grants Selected ou amplos no próprio agente (`Sites.Read.All`, `Files.Read.All`, `Sites.ReadWrite.All`) | Todos | Um grant próprio é caminho directo. Revogar um scope de nível superior não remove acesso dado por um scope inferior |
| Grants Selected em níveis inferiores (lista, item, ficheiro) | Todos | O acesso persiste depois de remover só o grant do site |
| Credenciais do blueprint partilhadas por identidades irmãs | Entra Agent ID | Quem detém a credencial obtém tokens para qualquer agent identity do blueprint |
| Agent user e fluxo OBO | Entra Agent ID | Sujeitos de token distintos. Política e desactivação da agent identity não os cobrem de forma documentada |
| Managed identity user-assigned reutilizada entre runtimes | Managed identity, blueprint federado | Um runtime com âmbito diferente usa o mesmo sujeito de token |
| Credencial Graph do executor acessível ao runtime do agente | Todos | Destrói a separação. O agente nunca recebe a credencial do executor |
| URLs pré-autenticados já emitidos | Todos | Descarregam sem token até 1 hora. O executor não os devolve ao agente |
| `$batch` e `/beta/$batch` | Todos | O pedido externo é sempre `POST`. Os métodos e caminhos reais vão no corpo (inferência) |
| `$expand` e `$select` em metadados | Todos | `$expand=children` lista filhos. `@microsoft.graph.downloadUrl` entrega a capacidade de descarregar |
| Pesquisa Graph (`/search/query`) | Todos | Comportamento com Selected NÃO VERIFICADO. Só há resposta de fórum a dizer que não respeita |
| Acessos locais ao recurso (membros do site, links de partilha) | Todos | O acesso efectivo do utilizador em OBO e os links existentes não passam pelo gateway |
| Chaves de API e segredos guardados no runtime | Todos | Contornam o Entra. Conditional Access não se aplica |
| Azure RBAC atribuído à identidade | Agent ID, service principal, managed identity | O Azure RBAC pode continuar a aceitar o token até expirar |
| Conectores, ferramentas MCP e Power Platform do agente | Fora do MVP | Podem ter ligação própria ao recurso |
| Identidade do APIM e edição de políticas | Gateway | Quem edita políticas pode usar a managed identity do APIM e exportar o token. A credencial de dados fica no executor, não no APIM |
| Sessões de utilizador abertas e refresh tokens | Agent user, OBO | `revokeSignInSessions` cobre refresh tokens, não access tokens |
| Egress do runtime para o Graph | Todos | Egress restrito é defesa adicional e não substitui remover permissões (especificação, secção 5) |

Limite estrutural verificado e inferido: não há documentação que permita restringir o Microsoft Graph a aceitar pedidos só de um APIM. Quem tiver um token Graph válido chama o Graph directamente. O que impede o contorno é o agente não possuir a credencial. Por isso a classificação `GatewayEnforced` depende de provar que o agente não tem credencial nem grant próprio, e não de regras de rede.

## 8. O que nunca se promete

Baseado nas secções 2 e 10 da especificação, e nas regras de honestidade da secção 0.

- Ausência absoluta de falhas. A promessa é substituída por requisitos verificáveis, defesa em profundidade, ensaios de falha e limites explícitos.
- "Acesso universal revogado". `verified_scoped` exige âmbito explícito e declara o recurso e as operações.
- Revogação instantânea de todos os tokens. Os tokens já emitidos têm janelas documentadas ou não documentadas, e o produto mede-as, não as presume.
- Conditional Access como revogação instantânea. Avalia-se na emissão e na renovação.
- CAE como garantia universal. Depende do recurso, do cliente e da identidade.
- Controlo de todos os agentes Copilot Studio, Foundry, MCP e de terceiros. Estão fora do MVP.
- Eliminar dados já exfiltrados.
- Cancelar universalmente operações já aceites por serviços externos. A revalidação não elimina a corrida com uma chamada já enviada ao Graph.
- Rollback universal e exactly-once universal para efeitos Graph.
- Converter `suspended` em `verified_scoped` automaticamente. Uma resposta 200, um dashboard verde ou uma demonstração com simulação não provam contenção.
- Prazo universal de contenção externa. Mede-se por identidade e por recurso.
- Cobertura de qualquer agente por Agent 365, Defender, Purview ou Sentinel, nem bloqueio síncrono de qualquer acção.
- Que a ledger impeça uma entrada inicial falsa, garanta disponibilidade ou recupere dados adulterados. Dá evidência de adulteração mediante verificação.
- Conformidade RGPD automática por usar serviços Microsoft.
- Que o APIM transforme a governação de modelos em autorização fina de ferramentas.

## 9. Lacunas que limitam os tectos

| Lacuna | Efeito | Como fechar |
| --- | --- | --- |
| Destino dos tokens já emitidos a agent identities e a service principals fora da CAE | O tecto `verified_scoped` de Agent ID e service principal depende da sonda | Fase D, BT-14, recurso de teste |
| CAE para agent identities | Sem ela, a janela pode ir até à expiração do token | Ler documentação futura. Teste em tenant |
| Disable de managed identity | A contenção assenta em grants e runtime | L11 em `permission-matrix.md` |
| Block no centro de administração Microsoft 365 contra `accountEnabled` | Dois interruptores sem relação documentada | L16 |
| Efeito em cascata sobre agent user e OBO | Pode deixar sujeitos de token activos | L06 |
| Inventário de caminhos alternativos no tenant | Sem ele a classificação é `Unverified` | Inventário na fase C |
| Tempo de propagação da revogação de grants Selected | O passo "grant removido" não tem prazo | Medir na fase C |
| Efeito da cache da managed identity sobre blueprint federado | Pode alargar a janela de revogação | Teste na fase D |
