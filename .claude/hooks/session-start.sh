#!/bin/bash
# SessionStart hook: installs the .NET 8, 9, and 10 SDKs in the background so
# Claude Code on the web sessions can build, test, and run dotnet format across
# every target framework before pushing.
#
# Async mode: the session starts immediately and the SDK install runs in the
# background. Claude may briefly be unable to run dotnet during the first few
# seconds of the session — wait or check for completion if needed.
set -euo pipefail

echo '{"async": true, "asyncTimeout": 600000}'

# Only run in the remote (Claude Code on the web) environment. Local sessions
# typically already have the SDKs installed.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

DOTNET_ROOT="${HOME}/.dotnet"
INSTALL_SCRIPT="${HOME}/.dotnet-install.sh"
CHANNELS=("8.0" "9.0" "10.0")

# Persist env vars first so they're available even if the install is still running.
{
  echo "export DOTNET_ROOT=\"${DOTNET_ROOT}\""
  echo "export PATH=\"${DOTNET_ROOT}:\${PATH}\""
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
  echo "export DOTNET_NOLOGO=1"
} >> "${CLAUDE_ENV_FILE}"

# Download the installer once if we don't already have it.
if [ ! -x "${INSTALL_SCRIPT}" ]; then
  curl -fsSL -o "${INSTALL_SCRIPT}" https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh
  chmod +x "${INSTALL_SCRIPT}"
fi

# Install each channel. dotnet-install.sh is idempotent — re-running with an
# already-installed channel is a no-op.
for channel in "${CHANNELS[@]}"; do
  if [ -x "${DOTNET_ROOT}/dotnet" ] && "${DOTNET_ROOT}/dotnet" --list-sdks 2>/dev/null | grep -q "^${channel}\."; then
    echo "[session-start] .NET ${channel} SDK already installed"
    continue
  fi
  echo "[session-start] Installing .NET ${channel} SDK to ${DOTNET_ROOT}"
  "${INSTALL_SCRIPT}" --channel "${channel}" --install-dir "${DOTNET_ROOT}" --no-path
done

echo "[session-start] Installed SDKs:"
"${DOTNET_ROOT}/dotnet" --list-sdks
