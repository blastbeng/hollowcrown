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

echo "== central endpoint checks =="
TS=$(date +%s)
U="tester_$TS"
CREDS="{\"user\":\"$U\",\"pass\":\"secret123\"}"
TOKEN=$(curl -sf -X POST http://127.0.0.1:6561/auth/register -H 'Content-Type: application/json' -d "$CREDS" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
[ -n "$TOKEN" ] || fail "auth/register" 2
AUTH="Authorization: Bearer $TOKEN"
curl -sf -X POST http://127.0.0.1:6561/auth/login -H 'Content-Type: application/json' -d "$CREDS" >/dev/null || fail "auth/login" 2
CHAR=$(curl -sf -X POST http://127.0.0.1:6561/characters -H "$AUTH" -H 'Content-Type: application/json' -d '{"name":"Ashen","classId":"warden"}')
echo "$CHAR" | grep -q '"classId":"warden"' || fail "characters create: $CHAR" 2
CID=$(echo "$CHAR" | sed -n 's/.*"id":\([0-9]*\).*/\1/p')
curl -sf -X PUT http://127.0.0.1:6561/characters/$CID/progress -H "$AUTH" -H 'Content-Type: application/json' -d '{"level":3,"xp":420,"gearJson":"[]"}' | grep -q '"level":3' || fail "progress save" 2
curl -sf http://127.0.0.1:6561/characters/$CID -H "$AUTH" | grep -q '"xp":420' || fail "progress reload" 2
curl -sf -X POST http://127.0.0.1:6561/servers/heartbeat -H 'Content-Type: application/json' \
  -d "{\"serverId\":\"test-$TS\",\"name\":\"Test Realm\",\"mode\":\"duel\",\"host\":\"127.0.0.1\",\"port\":7777,\"players\":1,\"maxPlayers\":2,\"hasPassword\":false}" >/dev/null || fail "heartbeat" 2
curl -sf "http://127.0.0.1:6561/servers?mode=duel" | grep -q "Test Realm" || fail "server list" 2
CODE=$(curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:6561/characters -H "Authorization: Bearer bad")
[ "$CODE" = "401" ] || fail "bad token must be rejected with 401 (got $CODE)" 2
echo "auth/characters/heartbeat endpoint checks OK"

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
