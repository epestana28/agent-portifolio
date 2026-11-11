# Segurança

## Estado

Este projecto é um protótipo. O lado Microsoft (Entra, Graph, SharePoint) está em simulação e o projecto não deve ser usado para proteger agentes em produção.

## Comunicar uma vulnerabilidade

Não abra uma issue pública. Use "Report a vulnerability" no separador Security do repositório no GitHub, que cria um aviso privado. Descreva o impacto, os passos para reproduzir e a versão ou o commit.

## Âmbito

Interessa em especial qualquer forma de: executar uma acção sem capacidade válida, executar depois de uma suspensão, obter uma decisão positiva com a base de estado indisponível, alterar ou apagar eventos de auditoria, agir sobre um agente de outro tenant, ou obter do executor uma credencial ou um URL pré-autenticado.

O modo de autenticação `Development` e os tokens emitidos por `devtoken` existem só para a pilha local e estão fora de âmbito.
