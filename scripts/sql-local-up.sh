#!/usr/bin/env bash
# Starts a throwaway SQL Server 2022 for the ledger tests and prints the AP_TEST_SQL line.
# Remove it with: docker rm -f ap-test-sql
set -euo pipefail
pw="Ap1!$(head -c 32 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 24)"
docker rm -f ap-test-sql >/dev/null 2>&1 || true
docker run -d --name ap-test-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$pw" -p 127.0.0.1:14333:1433 \
  mcr.microsoft.com/mssql/server:2022-latest >/dev/null
for _ in $(seq 1 60); do
  docker logs ap-test-sql 2>&1 | grep -q "Recovery is complete" && break
  sleep 2
done
echo "export AP_TEST_SQL='Server=127.0.0.1,14333;User Id=sa;Password=$pw;Encrypt=True;TrustServerCertificate=True'"
