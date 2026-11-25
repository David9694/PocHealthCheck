#!/usr/bin/env bash
set -euo pipefail

SERVICE_LABEL="com.pochealthcheck.workerweb"
PLIST_PATH="${HOME}/Library/LaunchAgents/${SERVICE_LABEL}.plist"
USER_DOMAIN="gui/$(id -u)"

echo "==> Zatrzymywanie i wyrejestrowywanie usługi '${SERVICE_LABEL}' z launchd..."
launchctl bootout "${USER_DOMAIN}/${SERVICE_LABEL}" 2>/dev/null || true

if [[ -f "${PLIST_PATH}" ]]; then
  rm -f "${PLIST_PATH}"
  echo "==> Usunięto plik: ${PLIST_PATH}"
fi

echo "==> Usługa macOS '${SERVICE_LABEL}' została odinstalowana."
