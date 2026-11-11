#!/bin/sh
# Selects the process by AP_SERVICE (or by the first argument).
set -eu
service="${AP_SERVICE:-}"
if [ "$#" -gt 0 ]; then
  case "$1" in
    control|executor|worker|demo|devtoken) service="$1"; shift ;;
  esac
fi

case "$service" in
  control)  exec dotnet /app/control/AgentPortifolio.ControlApi.dll "$@" ;;
  executor) exec dotnet /app/executor/AgentPortifolio.PolicyExecutor.dll "$@" ;;
  worker)   exec dotnet /app/worker/AgentPortifolio.ContainmentWorker.dll "$@" ;;
  demo)     exec dotnet /app/demo/AgentPortifolio.LocalSlice.dll "$@" ;;
  devtoken) exec dotnet /app/devtoken/AgentPortifolio.DevToken.dll "$@" ;;
  *)
    echo "AP_SERVICE must be one of: control, executor, worker, demo, devtoken." >&2
    exit 64 ;;
esac
