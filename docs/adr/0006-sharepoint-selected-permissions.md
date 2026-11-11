# ADR 0006: SharePoint com Selected permissions

Estado: proposto. Data de verificação das fontes: 2026-11-11.

## Contexto

O primeiro caso de uso é ler e listar documentos em SharePoint. As permissões de aplicação amplas (`Sites.Read.All`, `Files.Read.All`) dão acesso a todos os sites do tenant. Uma identidade com uma delas contorna qualquer limite que o produto imponha à frente.

Estado real: o catálogo de operações só tem `sharepoint.document.read` e `sharepoint.document.list`. O recurso canónico é `sharepoint:site/{siteId}/drive/{driveId}`. A reconciliação suspende o agente quando vê uma concessão ampla. Nenhum Graph real foi chamado: a fonte de grants e o gateway de documentos são simulados.

## Decisão

1. Usar `Sites.Selected` com o papel `read` para leitura. Escrita, e qualquer papel superior, fica fora do MVP.
2. O consentimento para a aplicação e a concessão no recurso são dois passos separados. Sem os dois e um token válido não há acesso.
3. A identidade que concede (grantor) é diferente da identidade executora. O grantor não lê documentos e o executor não concede.
4. Proibir `Sites.Read.All`, `Files.Read.All`, `Sites.ReadWrite.All`, `Sites.FullControl.All` e equivalentes amplos nas identidades de agente e executoras. A presença de uma suspende o agente na reconciliação.
5. Pontos de controlo de revogação em duas camadas: remover a concessão no recurso (ou revogar o consentimento) e o bloqueio local por epoch. Cada camada é verificada por sonda, não presumida.
6. Cada agente tem o seu `ExecutorObjectId`, por isso uma concessão ao executor de um agente não alarga outro.
7. O executor só aceita o recurso na forma canónica. Não normaliza. `$batch` e redireccionamentos 302 não são aceites como forma de contornar o catálogo de operações.
8. As contradições da documentação (ver abaixo) ficam como lacunas L-19 a L-21. Não se resolvem por suposição. Resolvem-se no tenant na fase C.

## Consequências

- O âmbito de risco fica no site ou na biblioteca concedidos, em vez de em todo o tenant.
- Remover concessões ao nível de lista ou item só está documentado em beta. No v1.0, a remoção documentada é ao nível do site, ou por revogação do consentimento. Isto limita a granularidade da revogação.
- A URL pré-autenticada de transferência (302) mantém validade depois de o acesso ser retirado. A validade está contraditória entre fontes (minutos contra 1 hora). Um agente que já tenha a URL consegue descarregar até ela expirar (T-06). O executor não deve devolver essa URL ao agente.
- O `$batch` aceita até 20 pedidos e devolve 200 externo com estados internos. O executor tem de inspeccionar o interior. É um dos vectores de bypass tratados no modelo de ameaças.
- Throttling com 429 e 503 e `Retry-After`. Não é permitido teste de carga em SharePoint Online, por isso o limite de débito não se valida por carga.
- O formato real dos identificadores de site é pressuposto (P-01).

## Alternativas rejeitadas

- `Sites.Read.All` com filtro no gateway: o agente ou o executor passam a poder ler tudo, e um erro no gateway expõe o tenant inteiro.
- Permissões delegadas em nome do utilizador: ligam o acesso ao utilizador, e o MVP é só em modo aplicação (ADR 0002).
- Uma identidade executora única para todos os agentes: uma concessão a um agente alarga todos.
- Link de partilha anónimo ou link de organização como mecanismo de acesso: fora do modelo e difícil de revogar.
- RBAC for Applications do Exchange para este caso: é para Exchange, não SharePoint.

## Evidência documental

Todas verificadas em 2026-11-11. As contradições estão assinaladas e não foram suavizadas.

- Selected permissions exigem consentimento, concessão no recurso e token. https://learn.microsoft.com/en-us/graph/permissions-selected-overview
- O papel `read` e o endpoint de permissões de site. https://learn.microsoft.com/en-us/graph/api/site-post-permissions?view=graph-rest-1.0
- Contradição: a página de leitura de itens lista `Files.Read.All` e `Sites.Read.All` entre as permissões menos privilegiadas, enquanto a visão geral de Selected diz que `Sites.Selected` basta. https://learn.microsoft.com/en-us/graph/api/driveitem-get?view=graph-rest-1.0
- Contradição: a concessão ao nível de lista e item exige `Sites.FullControl.All` numa página e `Sites.ReadWrite.All` noutra. https://learn.microsoft.com/en-us/graph/permissions-selected-overview
- A remoção de permissões ao nível do site é só aplicação e está em v1.0. A remoção por lista ou item só está em beta. https://learn.microsoft.com/en-us/graph/api/site-delete-permission?view=graph-rest-1.0
- A resposta 302 com URL pré-autenticada, validade "minutos" numa página e "1 hora" noutra, sem invalidação imediata. https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0
- `$batch`: até 20 pedidos, resposta externa 200 com estados internos. https://learn.microsoft.com/en-us/graph/json-batching
- Throttling com `Retry-After`, e teste de carga não permitido em SharePoint Online. https://learn.microsoft.com/en-us/sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online

## Estado

Proposto. A validação das contradições e dos efeitos da revogação fica para a fase C.
