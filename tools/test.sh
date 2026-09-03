#!/usr/bin/env bash
# test.sh — local verification: solution build, central server boot + /health,
# Godot headless boot. Exit codes: 1 build fail, 2 central fail, 3 godot fail.
set -uo pipefail
cd "$(dirname "$0")/.."
if command -v dotnet >/dev/null 2>&1; then DOTNET=dotnet
elif [ -x /opt/dotnet/dotnet ]; then DOTNET=/opt/dotnet/dotnet
elif [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"
else echo "TEST FAILED: dotnet SDK 8+ not found"; exit 1; fi

fail() { echo "TEST FAILED: $1"; exit "${2:-1}"; }

echo "== dotnet build =="
"$DOTNET" build Hollowcrown.sln -v minimal || fail "dotnet build" 1

echo "== central boot + /health (port 6561) =="
ASPNETCORE_URLS=http://127.0.0.1:6561 nohup "$DOTNET" run --project central \
  >/tmp/hc_test_central.log 2>&1 </dev/null &
CPID=$!
trap 'kill $CPID 2>/dev/null; true' EXIT
HEALTH=""
for _ in $(seq 1 30); do
  sleep 2
  HEALTH=$(curl -sf http://127.0.0.1:6561/health 2>/dev/null) && break
done
[ -n "$HEALTH" ] || { echo "-- central log --"; cat /tmp/hc_test_central.log; fail "central /health" 2; }
echo "central /health -> $HEALTH"

echo "== godot headless boot =="
GODOT_BIN="$(command -v godot || true)"
[ -z "$GODOT_BIN" ] && [ -x /opt/godot/bin/godot ] && GODOT_BIN=/opt/godot/bin/godot
[ -z "$GODOT_BIN" ] && [ -x /usr/local/bin/godot ] && GODOT_BIN=/usr/local/bin/godot
if [ -n "$GODOT_BIN" ]; then
  # mono module needs dotnet reachable: export DOTNET_ROOT from the resolved SDK
  export DOTNET_ROOT="$(dirname "$(command -v dotnet || echo /opt/dotnet/dotnet)")"
  export PATH="$PATH:/opt/dotnet:/usr/lib/dotnet"
  test -f game/.godot/global_script_class_cache.cfg || \
    timeout 300 "$GODOT_BIN" --headless --path game --import >/dev/null 2>&1
  timeout 90 "$GODOT_BIN" --headless --path game --quit || fail "godot headless boot" 3
  echo "godot headless boot OK"
else
  echo "godot not found on this machine — headless boot check skipped"
fi
echo "TEST OK"
