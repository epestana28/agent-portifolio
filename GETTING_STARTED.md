# Começar

## 1. Demonstração local (2 minutos, sem credenciais)

```bash
git clone https://github.com/epestana28/agent-portifolio.git
cd agent-portifolio
dotnet run --project samples/LocalSlice
```

Mostra: registo, aprovação a dois, bloqueio, suspensão, contenção, reactivação, auditoria.

## 2. Pilha Docker (10 minutos, com SQL Server)

```bash
git clone https://github.com/epestana28/agent-portifolio.git
cd agent-portifolio
./scripts/quickstart.sh
```

Levanta SQL Server, controlo, executor, worker. Testa o fluxo completo por HTTP.

## 3. Testes locais

```bash
dotnet test
```

73 testes de unidade e integração. 5 de ledger ignorados sem SQL.

## 4. Testes com SQL Server real

```bash
eval "$(./scripts/sql-local-up.sh)"
dotnet test
```

Todos os 78 testes, incluindo validação de ledger.

## 5. Modo Entra

```bash
docker compose -f docker-compose.yml -f docker-compose.entra.yml up -d
```

Com tenant Azure e aplicações registadas em Entra.

## 6. Contribuir

```bash
git clone https://github.com/epestana28/agent-portifolio.git
cd agent-portifolio
git checkout -b feat/seu-nome
dotnet build && dotnet test
git commit -m "tipo(âmbito): descrição"
git push origin feat/seu-nome
```

Leia CONTRIBUTING.md para as regras.
