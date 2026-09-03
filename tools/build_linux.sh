#!/usr/bin/env bash
# build_linux.sh — build the full solution (Linux native target)
set -euo pipefail
cd "$(dirname "$0")/.."
if command -v dotnet >/dev/null 2>&1; then DOTNET=dotnet
elif [ -x /opt/dotnet/dotnet ]; then DOTNET=/opt/dotnet/dotnet
elif [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"
else echo "ERROR: dotnet SDK 8+ not found (PATH, /opt/dotnet, ~/.dotnet)"; exit 1; fi
exec "$DOTNET" build Hollowcrown.sln "$@"
