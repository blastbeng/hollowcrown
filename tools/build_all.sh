#!/usr/bin/env bash
# build_all.sh — Debug + Release builds (fast correctness gate before commit)
set -euo pipefail
cd "$(dirname "$0")/.."
if command -v dotnet >/dev/null 2>&1; then DOTNET=dotnet
elif [ -x /opt/dotnet/dotnet ]; then DOTNET=/opt/dotnet/dotnet
elif [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"
else echo "ERROR: dotnet SDK 8+ not found (PATH, /opt/dotnet, ~/.dotnet)"; exit 1; fi
"$DOTNET" build Hollowcrown.sln
"$DOTNET" build Hollowcrown.sln -c Release
echo "BUILD ALL OK"
