# ADR 0001: Microsoft-first

Estado: proposto. Data de verificação das fontes: 2026-11-11.

## Contexto

O projecto constrói uma camada de controlo e evidência para agentes executados em Azure com acesso a Microsoft 365. A especificação manda aproveitar serviços Microsoft existentes antes de escrever componentes equivalentes (secção 3). Não é permitido criar criptomoeda, blockchain própria, emissor OAuth próprio nem sistema paralelo de identidade.

Estado real: a fase A foi escrita a partir de documentação oficial e sem tenant. A fase B (fatia local) está implementada com o mundo Microsoft simulado. As fases C, D e E não foram executadas, e não existe adaptador real para Entra nem para Graph.

## Decisão

Cada necessidade usa primeiro o serviço Microsoft correspondente. O código próprio fica limitado ao que a Microsoft não cobre.

| Necessidade | Serviço Microsoft | Código próprio admitido |
|---|---|---|
| Identidade de agente | Entra Agent ID, quando suportado | Adaptador e correlação |
| Workloads existentes | Managed identities e service principals | Adaptadores separados |
| Entrada governada | Azure API Management e políticas Entra | Validação de capacidades e recursos no executor |
| Permissões SharePoint | Selected permissions | Orquestração de concessão e verificação |
| Histórico verificável | Azure SQL Database Ledger | Esquema e eventos de domínio |
| Digests independentes | Blob imutável, com Confidential Ledger como opção | Agendamento e verificação complementar |
| Segredos | Key Vault | Nenhum cofre próprio |
| Observabilidade | Azure Monitor e OpenTelemetry, Agent 365 quando licenciado | Instrumentação e correlação |
| Infraestrutura | Bicep | Módulos do produto |

O produto constrói a carteira, a autorização determinística, os estados de contenção e a evidência de domínio. Não constrói identidade, ledger, gateway, cofre nem emissor de tokens.

Agent 365, Defender, Purview e Sentinel são complementos sujeitos a suporte e licenciamento. Não se presume cobertura de qualquer agente nem bloqueio síncrono de qualquer acção.

## Consequências

- O projecto fica dependente de serviços Microsoft e das suas licenças. A contradição sobre a licença de Conditional Access para agentes fica por resolver no tenant (risco R-04).
- Algumas funcionalidades relevantes estão em preview ou sem declaração de GA. Ver ADR 0009.
- O APIM não transforma a governação de tráfego de modelos em autorização fina de operações Graph. Essa parte é código próprio.
- Cada afirmação sobre a Microsoft tem de levar fonte e data. O que não está documentado fica marcado como NÃO VERIFICADO e não é suavizado.
- O produto só pode ser validado em tenant. Até lá, as fases C e D ficam bloqueadas e o mundo Microsoft simulado não conta como prova.

## Alternativas rejeitadas

- Emissor de identidade ou de OAuth próprio: duplica o Entra e cria um segundo sistema de identidade, que a especificação proíbe.
- Cadeia de hashes ou blockchain própria para a evidência: ver ADR 0004.
- Gateway ou proxy de terceiros no lugar do APIM: não é Microsoft-first e não resolve a falta de autorização fina de operações Graph. Fica por justificar.
- Conditional Access como mecanismo único de contenção: as políticas de agentes não alteram objectos, não impedem a criação de identidades, e não se aplicam a acessos que não passem pela emissão de tokens do Entra (por exemplo chave de API).
- Esperar por um produto Microsoft que cubra tudo: a documentação lida não mostra capacidade de carteira de capacidades por agente, nem de ledger de revogação, nem de inspecção de `$batch` ou de redireccionamentos 302.

## Evidência documental

Todas verificadas em 2026-11-11.

- Agent ID: "available for all Microsoft Entra customers". Estado GA textual NÃO VERIFICADO. https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id
- Agent 365 em GA desde 2026-05-01, segmento Commercial, licença por utilizador. https://learn.microsoft.com/en-us/microsoft-agent-365/overview
- O AI gateway do APIM "extends API Management's existing API gateway; it's not a separate offering", e governa tráfego de modelos e ferramentas MCP e A2A. A ausência de capacidade para semântica de Graph é inferência por ausência na página. https://learn.microsoft.com/en-us/azure/api-management/genai-gateway-capabilities
- Conditional Access não se aplica a acesso por chave de API, que ignora a emissão de tokens do Entra. https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id
- Políticas de Conditional Access de âmbito tenant "doesn't prevent the creation of agent identities". https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities
- Licenciamento de Agent 365 e Conditional Access para agentes, com contradição entre páginas. https://learn.microsoft.com/en-us/entra/identity/conditional-access/agent-id e https://learn.microsoft.com/en-us/entra/agent-id/disable-agent-identities

## Estado

Proposto. Aprovação humana por obter no gate da fase A.
