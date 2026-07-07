#!/bin/sh
# HeroParser CLI Installer for macOS and Linux
# Installs the native AOT-compiled binary to /usr/local/bin (or ~/.local/bin)
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/KoalaFacts/HeroParser/main/install.sh | sh
#

set -e

OWNER="KoalaFacts"
REPO="HeroParser"
DEFAULT_VERSION="2.6.0"

echo "=================================================="
echo "          HeroParser CLI Native Installer          "
echo "=================================================="

# Detect OS and Architecture
OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
ARCH="$(uname -m)"

case "$OS" in
  darwin)
    OS_NAME="osx"
    ;;
  linux)
    OS_NAME="linux"
    ;;
  *)
    echo "Error: Unsupported operating system: $OS"
    exit 1
    ;;
esac

case "$ARCH" in
  x86_64|amd64)
    ARCH_NAME="x64"
    ;;
  arm64|aarch64)
    # Check if Arm64 Linux is requested (we currently only build Arm64 macOS and x64 Linux)
    if [ "$OS_NAME" = "linux" ]; then
      echo "Error: Arm64 architecture is currently not supported on Linux. Installing x64 binary under emulation is not recommended."
      exit 1
    fi
    ARCH_NAME="arm64"
    ;;
  *)
    echo "Error: Unsupported CPU architecture: $ARCH"
    exit 1
    ;;
esac

# Resolve version
echo "Fetching latest release version from GitHub..."
LATEST_VERSION=$(curl -s "https://api.github.com/repos/$OWNER/$REPO/releases/latest" | grep -i '"tag_name":' | sed -E 's/.*"([^"]+)".*/\1/' || echo "")

if [ -n "$LATEST_VERSION" ] && [ "$LATEST_VERSION" != "null" ]; then
  VERSION="${LATEST_VERSION#v}"
  echo "Latest version found: v$VERSION"
else
  VERSION="$DEFAULT_VERSION"
  echo "Fallback to default release version: v$VERSION"
fi

TAR_FILE="heroparser-v$VERSION-$OS_NAME-$ARCH_NAME.tar.gz"
URL="https://github.com/$OWNER/$REPO/releases/download/v$VERSION/$TAR_FILE"

# Create a temporary directory
TMP_DIR="$(mktemp -d)"
clean_up() {
  rm -rf "$TMP_DIR"
}
trap clean_up EXIT

echo "Downloading $URL..."
if ! curl -fsSL "$URL" -o "$TMP_DIR/$TAR_FILE"; then
  echo "Error: Failed to download release asset from $URL"
  exit 1
fi

echo "Extracting..."
tar -xzf "$TMP_DIR/$TAR_FILE" -C "$TMP_DIR"

if [ ! -f "$TMP_DIR/heroparser" ]; then
  echo "Error: Extracted archive did not contain the 'heroparser' executable."
  exit 1
fi

# Determine install folder
INSTALL_DIR="/usr/local/bin"
USE_SUDO=false

if [ ! -w "$INSTALL_DIR" ]; then
  if [ "$(id -u)" -eq 0 ]; then
    # Running as root, should be writeable
    true
  elif command -v sudo >/dev/null 2>&1; then
    # Non-root but sudo is available
    USE_SUDO=true
  else
    # Fallback to local user bin
    INSTALL_DIR="$HOME/.local/bin"
    mkdir -p "$INSTALL_DIR"
    echo "Warning: /usr/local/bin is not writeable and sudo is not available."
    echo "Installing to user-local directory: $INSTALL_DIR"
    
    # Prompt user to update PATH if necessary
    case ":$PATH:" in
      *:$INSTALL_DIR:*) ;;
      *)
        echo "=================================================="
        echo "WARNING: $INSTALL_DIR is not in your PATH."
        echo "To run the CLI, add it to your shell configuration:"
        echo "  export PATH=\"\$PATH:$INSTALL_DIR\""
        echo "=================================================="
        ;;
    esac
  fi
fi

echo "Installing binary to $INSTALL_DIR/heroparser..."
if [ "$USE_SUDO" = true ]; then
  sudo cp "$TMP_DIR/heroparser" "$INSTALL_DIR/heroparser"
  sudo chmod +x "$INSTALL_DIR/heroparser"
else
  cp "$TMP_DIR/heroparser" "$INSTALL_DIR/heroparser"
  chmod +x "$INSTALL_DIR/heroparser"
fi

echo "--------------------------------------------------"
echo "HeroParser CLI installed successfully!"
echo "Run 'heroparser --help' to get started."
echo "=================================================="
