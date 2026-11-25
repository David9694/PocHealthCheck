#!/usr/bin/env bash
set -euo pipefail

SERVICE_LABEL="com.pochealthcheck.workerweb"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PROJECT_PATH="${REPO_ROOT}/PocHealthCheck.WorkerWeb/PocHealthCheck.WorkerWeb.csproj"
PUBLISH_DIR="${REPO_ROOT}/publish/workerweb"
LOG_DIR="${HOME}/Library/Logs/PocHealthCheck.WorkerWeb"
LAUNCH_AGENTS_DIR="${HOME}/Library/LaunchAgents"
PLIST_PATH="${LAUNCH_AGENTS_DIR}/${SERVICE_LABEL}.plist"

DOTNET_BIN="$(command -v dotnet || true)"
if [[ -z "${DOTNET_BIN}" && -x "/usr/local/share/dotnet/dotnet" ]]; then
  DOTNET_BIN="/usr/local/share/dotnet/dotnet"
elif [[ -z "${DOTNET_BIN}" && -x "/opt/homebrew/bin/dotnet" ]]; then
  DOTNET_BIN="/opt/homebrew/bin/dotnet"
fi

if [[ -z "${DOTNET_BIN}" ]]; then
  echo "Błąd: nie znaleziono polecenia 'dotnet'." >&2
  exit 1
fi

# Wyznaczenie DOTNET_ROOT (wymagane przez launchd na macOS, który ma minimalny PATH)
DOTNET_REAL="$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "${DOTNET_BIN}")"
DOTNET_ROOT_DIR="$(dirname "${DOTNET_REAL}")"

echo "==> Publikowanie PocHealthCheck.WorkerWeb (Release) do: ${PUBLISH_DIR}"
"${DOTNET_BIN}" publish "${PROJECT_PATH}" -c Release -o "${PUBLISH_DIR}"

mkdir -p "${LOG_DIR}" "${LAUNCH_AGENTS_DIR}"

echo "==> Generowanie definicji usługi launchd: ${PLIST_PATH}"
cat > "${PLIST_PATH}" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>${SERVICE_LABEL}</string>

    <key>ProgramArguments</key>
    <array>
        <string>${PUBLISH_DIR}/PocHealthCheck.WorkerWeb</string>
    </array>

    <key>WorkingDirectory</key>
    <string>${PUBLISH_DIR}</string>

    <key>EnvironmentVariables</key>
    <dict>
        <key>DOTNET_ROOT</key>
        <string>${DOTNET_ROOT_DIR}</string>
        <key>ASPNETCORE_ENVIRONMENT</key>
        <string>Production</string>
        <key>ASPNETCORE_URLS</key>
        <string>http://localhost:5200</string>
    </dict>

    <key>RunAtLoad</key>
    <true/>

    <key>KeepAlive</key>
    <true/>

    <key>ThrottleInterval</key>
    <integer>5</integer>

    <key>StandardOutPath</key>
    <string>${LOG_DIR}/worker.out.log</string>

    <key>StandardErrorPath</key>
    <string>${LOG_DIR}/worker.err.log</string>
</dict>
</plist>
EOF

USER_DOMAIN="gui/$(id -u)"

echo "==> Rejestrowanie i uruchamianie usługi '${SERVICE_LABEL}' w launchd (${USER_DOMAIN})..."
launchctl bootout "${USER_DOMAIN}/${SERVICE_LABEL}" 2>/dev/null || true
launchctl bootstrap "${USER_DOMAIN}" "${PLIST_PATH}"
launchctl kickstart -k "${USER_DOMAIN}/${SERVICE_LABEL}"

sleep 2
echo "==> Usługa została zainstalowana i uruchomiona w tle!"
echo "    - Status usługi:  ${SCRIPT_DIR}/status-macos-service.sh"
echo "    - Logi stdout:    ${LOG_DIR}/worker.out.log"
echo "    - Logi stderr:    ${LOG_DIR}/worker.err.log"
echo "    - HealthCheck:    http://localhost:5200/health"
