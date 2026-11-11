#!/usr/bin/env bash
# Starts the local stack and walks the whole slice over HTTP:
# register -> approve (second person) -> capability -> read -> suspend -> refused -> containment report.
#
# Auth is in DEVELOPMENT mode and the Microsoft side is SIMULATED. Nothing here is
# evidence about Entra, Graph or SharePoint.
set -euo pipefail
cd "$(dirname "$0")/.."

rand() { head -c 32 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c "$1"; }
uuid() { cat /proc/sys/kernel/random/uuid; }

if [ ! -f .env ]; then
  umask 077
  printf 'AP_SQL_PASSWORD=Ap1!%s\nAP_DEV_SIGNING_KEY=%s\nAP_TENANT_ID=%s\n' "$(rand 24)" "$(rand 40)" "$(uuid)" > .env
  echo "Created .env with random local values."
fi
# shellcheck disable=SC1091
. ./.env

EXECUTOR_OID="${AP_EXECUTOR_OBJECT_ID:-0e0e0e0e-0000-4000-8000-000000000001}"
CONTROL=http://127.0.0.1:8081
EXECUTOR=http://127.0.0.1:8082
SITE='contoso.sharepoint.com,11111111-1111-4111-8111-111111111111,22222222-2222-4222-8222-222222222222'
DRIVE='b!ApprovedDrive'

docker compose up -d
printf 'Waiting for the services'
for url in "$CONTROL/healthz" "$EXECUTOR/healthz"; do
  for _ in $(seq 1 90); do
    curl -fsS -o /dev/null "$url" 2>/dev/null && break
    printf '.'; sleep 2
  done
  curl -fsS -o /dev/null "$url" || { echo; echo "Service at $url did not start. See: docker compose logs"; exit 1; }
done
echo ' ok'

token() { docker compose run --rm --no-deps -T control devtoken "$@" | tr -d '\r\n'; }
ADMIN_A=$(token control "$AP_TENANT_ID" "$(uuid)" AgentPortifolio.PolicyAdmin)
ADMIN_B=$(token control "$AP_TENANT_ID" "$(uuid)" AgentPortifolio.PolicyAdmin)
INCIDENT=$(token control "$AP_TENANT_ID" "$(uuid)" AgentPortifolio.IncidentOperator)
AUDITOR=$(token control "$AP_TENANT_ID" "$(uuid)" AgentPortifolio.Auditor)
AGENT_OID=$(uuid)
AGENT=$(token executor "$AP_TENANT_ID" "$AGENT_OID")

# call <token> <method> <url> [json]: prints "<http status> <body>"
call() {
  local t=$1 m=$2 u=$3 d=${4:-}
  if [ -n "$d" ]; then
    curl -sS -o /tmp/ap-body.$$ -w '%{http_code}' -X "$m" -H "Authorization: Bearer $t" -H 'Content-Type: application/json' -d "$d" "$u"
  else
    curl -sS -o /tmp/ap-body.$$ -w '%{http_code}' -X "$m" -H "Authorization: Bearer $t" "$u"
  fi
  printf ' '; cat /tmp/ap-body.$$; rm -f /tmp/ap-body.$$; echo
}
step() { echo; echo "== $*"; }
expires() { date -u -d "+$1 days" +%Y-%m-%dT%H:%M:%SZ; }
read_body() { printf '{"operation":"sharepoint.document.read","arguments":{"siteId":"%s","driveId":"%s","itemId":"01ITEM"},"idempotencyKey":"%s"}' "$SITE" "$DRIVE" "$(uuid)"; }

step "1. Register the agent (PolicyAdmin A)"
OUT=$(call "$ADMIN_A" POST "$CONTROL/agents" "{\"identityType\":\"EntraAgentId\",\"identityObjectId\":\"$AGENT_OID\",\"blueprintId\":null,\"executorObjectId\":\"$EXECUTOR_OID\",\"runtime\":\"container-app/pilot\",\"purpose\":\"read approved documents\",\"ownerObjectId\":\"$(uuid)\",\"sponsorObjectId\":null,\"contact\":\"owner@example.invalid\",\"maxActionsPerHour\":100,\"walletExpiresAt\":\"$(expires 30)\"}")
echo "$OUT"
AGENT_ID=$(printf '%s' "$OUT" | sed -n 's/.*"id":"\([0-9a-f-]\{36\}\)".*/\1/p' | head -1)
[ -n "$AGENT_ID" ] || { echo "Registration failed."; exit 1; }

step "2. Submit, then approve: the same person is refused, a second person activates"
call "$ADMIN_A" POST "$CONTROL/agents/$AGENT_ID/submit" | cut -c1-120
call "$ADMIN_A" POST "$CONTROL/agents/$AGENT_ID/approve"
call "$ADMIN_B" POST "$CONTROL/agents/$AGENT_ID/approve" | cut -c1-120

step "3. Grant one capability: read, one library, 7 days"
call "$ADMIN_A" POST "$CONTROL/agents/$AGENT_ID/capabilities" "{\"action\":\"sharepoint.document.read\",\"resource\":\"sharepoint:site/$SITE/drive/$DRIVE\",\"expiresAt\":\"$(expires 7)\"}"

step "4. The agent reads the approved document through the executor"
call "$AGENT" POST "$EXECUTOR/actions" "$(read_body)"

step "5. Incident: suspend (IncidentOperator)"
call "$INCIDENT" POST "$CONTROL/agents/$AGENT_ID/suspend" '{"reason":"anomalous behaviour"}'

step "6. The agent tries again: refused with agent_suspended"
call "$AGENT" POST "$EXECUTOR/actions" "$(read_body)"

step "7. Containment report after the worker ran (SIMULATED external steps)"
sleep 6
call "$AUDITOR" GET "$CONTROL/agents/$AGENT_ID/containments"

step "8. Audit trail (Auditor only), stored in the SQL ledger table ap.AuditEvent"
call "$AUDITOR" GET "$CONTROL/agents/$AGENT_ID/audit" | tr '{' '\n' | sed -n 's/.*"eventType":"\([^"]*\)".*"decision":\("[^"]*"\|null\),"reasonCode":\("[^"]*"\|null\).*/   \1 \2 \3/p'

echo
echo "Done. Stop with: docker compose down   (add -v to delete the database volume)"
