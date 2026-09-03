#!/usr/bin/env bash
# setup.sh — environment sanity check + package restore (project_vision.md Section 9)
set -euo pipefail
cd "$(dirname "$0")/.."
if command -v dotnet >/dev/null 2>&1; then DOTNET=dotnet
elif [ -x /opt/dotnet/dotnet ]; then DOTNET=/opt/dotnet/dotnet
elif [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"
else echo "ERROR: dotnet SDK 8+ not found (install .NET 8 SDK)"; exit 1; fi
echo "dotnet: $($DOTNET --version)"

GODOT_BIN="$(command -v godot || true)"
[ -z "$GODOT_BIN" ] && [ -x /opt/godot/bin/godot ] && GODOT_BIN=/opt/godot/bin/godot
[ -z "$GODOT_BIN" ] && [ -x /usr/local/bin/godot ] && GODOT_BIN=/usr/local/bin/godot
if [ -n "$GODOT_BIN" ]; then
  echo "godot:  $($GODOT_BIN --version)"
else
  echo "godot:  not found on this machine (ok for build; needed to run the game)"
fi
"$DOTNET" restore Hollowcrown.sln
echo "SETUP OK"
