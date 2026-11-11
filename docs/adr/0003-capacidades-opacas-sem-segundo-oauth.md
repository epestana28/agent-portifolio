# ADR 0003: Capacidades opacas, sem segundo OAuth

Estado: proposto. Data de verificação das fontes: 2026-11-11.

## Contexto

O produto precisa de restringir o que cada agente pode fazer para além do que o Entra concede. É preciso vincular a decisão a tenant, agente, acção, recurso, validade e `revocation_epoch`, e poder retirá-la logo que o agente é suspenso. A especificação proíbe criar um segundo JWT ou OAuth sem necessidade e manda preferir o Entra para autenticação.

Estado real: o `Capability` do código é um registo interno com `Id` opaco, ligado a tenant, agente, acção, recurso canónico, validade e `IssuedEpoch`. O agente não apresenta a capacidade: o servidor selecciona-a do estado a partir do agente autenticado, da operação e do recurso. Isto está implementado na fatia local (fase B) e não foi testado contra APIM nem Entra reais.

## Decisão

1. O Entra autentica. O APIM valida o token (`validate-azure-ad-token`, com `audiences` e aplicação cliente obrigatórias no modelo de política). O executor volta a validar o token e lê o estado autoritativo.
2. Uma capacidade é uma referência opaca interna, registada em SQL. Não é JWT nem token ao portador, e não tem valor fora do estado que a contém.
3. A capacidade vale só para o tenant, agente, acção, recurso canónico, validade e epoch com que foi emitida. Uma capacidade de epoch anterior morre com a suspensão e tem de ser reemitida.
4. Uma capacidade nunca é credencial de acesso a dados. Os dados só se alcançam através do executor, que detém a credencial Graph isolada.
5. A decisão não usa modelo de linguagem, nem cache positiva (ADR 0007).

## Consequências

- Não há verificação offline de capacidades: cada admissão lê o estado em SQL. A disponibilidade do SQL passa a ser a do produto (ADR 0007).
- A revogação é imediata para o gateway: incrementar a epoch invalida todas as capacidades anteriores numa transacção, sem esperar pela expiração de nenhum token.
- Um token do agente roubado não traz capacidades próprias. Continua válido no Entra até expirar, e contra o Graph directo só serve se o agente tiver grant directo (T-02).
- Sem emissor próprio, não há chaves de assinatura nem JWKS próprios para gerir.
- A confiança no APIM é limitada: quem edita políticas APIM pode contorná-lo (risco R-06). O executor tem de validar o token por si.

## Alternativas rejeitadas

- JWT assinado pelo produto como capacidade: cria um segundo emissor, exige chaves, e uma revogação antes da expiração obriga a consultar estado, o que anula a vantagem da verificação offline.
- Entregar ao agente o token Graph do executor: viola a fronteira de confiança e abre o roubo de credencial (T-02).
- Codificar cada capacidade como scope ou app role no Entra: duplica a autorização no Entra, tem granularidade grosseira e propagação lenta de alterações.
- Chaves de subscrição do APIM como identidade: não são autenticação de agente e não ligam a nenhum tenant nem agente.
- Confiar só no APIM para validar o token: a Microsoft recomenda validar sempre no backend.

## Evidência documental

Todas verificadas em 2026-11-11.

- `validate-azure-ad-token`: só `tenant-id` é obrigatório, `audiences` e `client-application-ids` são opcionais na política. A exigência de os usar é inferência deste projecto. https://learn.microsoft.com/en-us/azure/api-management/validate-azure-ad-token-policy
- "Always validate tokens in the backend API, even if the gateway validates them" e "Gateway configuration can change and tokens can be replayed". Lida por excerto. https://learn.microsoft.com/en-us/entra/msidweb/advanced/api-gateways#follow-best-practices
- Para o recurso: "Ensure applications revoke their own session tokens and stop accepting Microsoft Entra access tokens even if they're still valid". https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access
- Quem edita políticas APIM pode usar a managed identity do serviço, e o APIM "does not validate which backend the token is sent to". Lida por excerto. https://learn.microsoft.com/en-us/azure/api-management/api-management-howto-use-managed-service-identity#security-considerations-for-managed-identities
- Os mecanismos de rede do APIM aplicam-se ao troço gateway para backend. A ausência de forma de restringir o Graph a pedidos de um APIM é inferência. Lida por excerto. https://learn.microsoft.com/en-us/azure/api-management/secure-api-management#network-security

## Estado

Proposto. A validação do APIM real e dos claims do token fica para a fase C.
