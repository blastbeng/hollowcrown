#!/usr/bin/env bash
# balance_harness.sh — Vision 7 balance harness: boot 1-2 headless combat
# bots (--bot), let them fight through the server-authoritative combat path,
# then print a per-attacker winrate matrix parsed from the authority log
# (KILL lines). No GUI, no MCP: exits 0 on a clean run, 3 on a harness fault.
# Usage: tools/balance_harness.sh [duration_seconds] [-- classes a+b]
set -uo pipefail
cd "$(dirname "$0")/.."

DURATION=20
CLASSES="warden+nightblade"
if [[ "${1:-}" =~ ^[0-9]+$ ]]; then DURATION=$1; shift; fi
if [[ "${1:-}" = "--" ]]; then shift; CLASSES=${1:-$CLASSES}; fi

if command -v godot >/dev/null 2>&1; then GODOT_BIN=$(command -v godot)
elif [ -x /opt/godot/bin/godot ]; then GODOT_BIN=/opt/godot/bin/godot
elif [ -x /usr/local/bin/godot ]; then GODOT_BIN=/usr/local/bin/godot
else echo "HARNESS FAILED: godot binary not found"; exit 3; fi
if command -v dotnet >/dev/null 2>&1; then DOTNET=dotnet
elif [ -x /opt/dotnet/dotnet ]; then DOTNET=/opt/dotnet/dotnet
else echo "HARNESS FAILED: dotnet SDK not found"; exit 3; fi
export DOTNET_ROOT="$(dirname "$DOTNET")"   # mono module needs hostfxr reachable
export PATH="$DOTNET_ROOT:$PATH"

"$DOTNET" build Hollowcrown.sln -v minimal >/dev/null || { echo "HARNESS FAILED: build"; exit 3; }

LOG=$(mktemp)
# Wall-clock guard: a hung engine (or a quit timer starved by error spam)
# must never wedge CI — the timeout kills it, exit 124 reports the fault.
timeout $((DURATION + 60)) env DOTNET_ROOT="$DOTNET_ROOT" PATH="$PATH" \
  "$GODOT_BIN" --headless --path game -- --bot "--bot-classes=$CLASSES" \
  "--quit-after=$DURATION" >"$LOG" 2>&1
EC=$?
if [ $EC -ne 0 ]; then
  echo "HARNESS FAILED: godot exit $EC"; tail -30 "$LOG"; rm -f "$LOG"; exit 3
fi

if ! grep -q "BOT HARNESS READY" "$LOG"; then
  echo "HARNESS FAILED: bots never booted"; tail -30 "$LOG"; rm -f "$LOG"; exit 3
fi

DURATION_KILLS=$(grep -c "AUTHORITY: KILL" "$LOG")
echo "== HARNESS LOG (kills=$DURATION_KILLS over ${DURATION}s, classes=$CLASSES) =="
grep -E "BOT HARNESS|BOT READY|BOT ATTACK|KILL|RESPAWNED|down" "$LOG" | head -60

echo ""
echo "== WINRATE MATRIX (kills per attacker, parsed from authority log) =="
printf "%-22s %8s\n" "attacker" "kills"
for BOT in $(grep -oE 'KILL attacker=[^ ]+' "$LOG" | sed 's/KILL attacker=//' | sort -u); do
  K=$(grep -c "KILL attacker=$BOT" "$LOG")
  printf "%-22s %8s\n" "$BOT" "$K"
done
if [ "$DURATION_KILLS" -eq 0 ]; then
  echo "(no kills this window — raise the duration or check BOT ATTACK lines)"
fi
rm -f "$LOG"
echo "HARNESS OK"
