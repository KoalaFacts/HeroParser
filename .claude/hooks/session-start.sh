#!/bin/bash
# SessionStart hook: installs the .NET 8, 9, and 10 SDKs plus the PowerShell
# global tool (pwsh) in the background so Claude Code on the web sessions can
# build, test, and run dotnet format across every target framework before
# pushing. pwsh is required by the PowerShell-based PostToolUse/PreToolUse hooks
# in settings.json — without it, those hooks cannot run in this Linux env.
#
# Async mode: the session starts immediately and the install runs in the
# background. Claude may briefly be unable to run dotnet or pwsh during the
# first few seconds of the session — wait or check for completion if needed.
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
  echo "export PATH=\"${DOTNET_ROOT}:${DOTNET_ROOT}/tools:\${PATH}\""
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

# Install PowerShell (pwsh) as a .NET global tool so the PowerShell-based
# PostToolUse/PreToolUse hooks can run here. Global tools install to
# ${DOTNET_ROOT}/tools (already added to PATH above). Guarded for idempotency:
# `dotnet tool install` exits non-zero if the tool already exists, which would
# abort the script under `set -e`.
if "${DOTNET_ROOT}/dotnet" tool list --global 2>/dev/null | grep -qi "powershell"; then
  echo "[session-start] PowerShell global tool already installed"
else
  echo "[session-start] Installing PowerShell (pwsh) global tool"
  "${DOTNET_ROOT}/dotnet" tool install --global PowerShell \
    || echo "[session-start] WARNING: PowerShell tool install failed"
fi

echo "[session-start] Installed SDKs:"
"${DOTNET_ROOT}/dotnet" --list-sdks

echo "[session-start] pwsh version:"
"${DOTNET_ROOT}/tools/pwsh" --version \
  || echo "[session-start] WARNING: pwsh not found after install"
