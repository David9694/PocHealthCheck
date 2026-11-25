#!/usr/bin/env bash
set -euo pipefail

SERVICE_LABEL="com.pochealthcheck.workerweb"
LOG_DIR="${HOME}/Library/Logs/PocHealthCheck.WorkerWeb"
USER_DOMAIN="gui/$(id -u)"

echo "=== Status launchd (${USER_DOMAIN}/${SERVICE_LABEL}) ==="
if launchctl print "${USER_DOMAIN}/${SERVICE_LABEL}" >/tmp/pochealth_launchctl.txt 2>&1; then
  grep -E "state =|pid =|last exit code =" /tmp/pochealth_launchctl.txt || cat /tmp/pochealth_launchctl.txt
  rm -f /tmp/pochealth_launchctl.txt
else
  rm -f /tmp/pochealth_launchctl.txt
  echo "Usługa nie jest obecnie załadowana w launchd."
fi

echo ""
echo "=== Ostatnie wpisy z logów (${LOG_DIR}/worker.out.log) ==="
if [[ -f "${LOG_DIR}/worker.out.log" ]]; then
  tail -n 15 "${LOG_DIR}/worker.out.log"
else
  echo "Brak pliku logów."
fi

echo ""
echo "=== Sprawdzenie endpointu HTTP http://localhost:5200/health ==="
if curl -fsS "http://localhost:5200/health" >/dev/null 2>&1; then
  echo "HTTP 200 OK — Worker odpowiada na http://localhost:5200/health"
else
  echo "Endpoint http://localhost:5200/health nie odpowiada (lub usługa zgłasza Unhealthy HTTP 503)."
fi
