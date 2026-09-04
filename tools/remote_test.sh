#!/usr/bin/env bash
# remote_test.sh — one command: sync + boot the remote test environment
# (aider_continue.md Section 1). Status tokens:
#   OK | GODOT_RUNNING | HOST_OFFLINE | SYNC_FAILED | GODOT_START_FAILED | CENTRAL_FAILED
# Use --restart to kill and relaunch the remote editor (stale code).
set -u

HOST=192.168.1.29
RUSER=blast
KEY="$HOME/.ssh/id_ed25519"
REMOTE_DIR=/opt/projects/hollowcrown
GAME_DIR="$REMOTE_DIR/game"
CENTRAL_LOG=/tmp/hollowcrown_central.log
GODOT_LOG=/tmp/hollowcrown_godot.log
RESTART=0
[ "${1:-}" = "--restart" ] && RESTART=1

sshrun() { ssh -i "$KEY" -o ConnectTimeout=6 -o BatchMode=yes "$RUSER@$HOST" "$@"; }
port_up() { sshrun "ss -tln | grep -q ':$1 '" >/dev/null 2>&1; }

# 1) host + key check. "Permission denied (publickey)" = host booted into another
#    OS that does not accept our keys: report offline, never touch the keys.
OUT=$(sshrun 'echo online' 2>&1) || { echo "HOST_OFFLINE: $OUT"; exit 2; }

# 2) clone-or-pull (auto-commit dirty remote tree before pulling)
if ! sshrun "test -d $REMOTE_DIR/.git" >/dev/null 2>&1; then
  ORIGIN=$(git -C "$(cd "$(dirname "$0")/.." && pwd)" remote get-url origin 2>/dev/null \
    || echo "https://github.com/blastbeng/hollowcrown")
  sshrun "git config --global init.defaultBranch main >/dev/null 2>&1; \
          git config --global user.email 'blast@remote' >/dev/null 2>&1; \
          git config --global user.name 'blast' >/dev/null 2>&1; \
          git clone $ORIGIN $REMOTE_DIR" >/dev/null 2>&1 \
    || { echo "SYNC_FAILED: could not clone $ORIGIN"; exit 3; }
else
  if ! sshrun "cd $REMOTE_DIR && git pull" >/dev/null 2>&1; then
    sshrun "cd $REMOTE_DIR && git config user.email 'blast@remote' >/dev/null 2>&1; \
            git config user.name 'blast' >/dev/null 2>&1; \
            git add -A && git commit -m 'auto: checkpoint before sync' >/dev/null 2>&1; \
            git pull" >/dev/null 2>&1 \
      || sshrun "cd $REMOTE_DIR && git pull --rebase" >/dev/null 2>&1 \
      || { echo "SYNC_FAILED: pull (try Section 4 manual commands)"; exit 3; }
  fi
fi

# 3) build the solution on the remote so the editor can load C# scripts
BUILD_OUT=$(sshrun "cd $REMOTE_DIR && export DOTNET_ROOT=/usr/lib/dotnet; \
          export PATH=\"\$PATH:/usr/lib/dotnet:/opt/dotnet\"; \
          dotnet build Hollowcrown.sln -v minimal 2>&1") \
  || { echo "SYNC_FAILED: remote dotnet build:"; echo "$BUILD_OUT" | tail -20; exit 3; }

# 4) central server on :6560
if ! port_up 6560; then
  sshrun "cd $REMOTE_DIR && ASPNETCORE_URLS=http://0.0.0.0:6560 \
          nohup dotnet run --project central >$CENTRAL_LOG 2>&1 </dev/null &" >/dev/null 2>&1
  UP=0
  for _ in $(seq 1 15); do sleep 3; port_up 6560 && { UP=1; break; }; done
  if [ "$UP" != 1 ]; then
    echo "CENTRAL_FAILED (log tail:)"; sshrun "tail -15 $CENTRAL_LOG" 2>/dev/null; exit 4
  fi
fi

# 5) Godot editor + playtester bridge on :6550 (Wayland session)
if [ "$RESTART" = 1 ]; then
  sshrun "pkill -x godot >/dev/null 2>&1; true"; sleep 3
fi
if ! port_up 6550; then
  # Ensure editor caches exist BEFORE the editor boots (the MCP bridge autoload
  # needs the class_name cache; without it its GDScript fails to parse).
  sshrun "test -f $GAME_DIR/.godot/global_script_class_cache.cfg" >/dev/null 2>&1 || \
    sshrun "export DOTNET_ROOT=/usr/lib/dotnet; \
            export PATH=\"\$PATH:/usr/lib/dotnet:/opt/dotnet\"; \
            timeout 300 /usr/local/bin/godot --headless --path $GAME_DIR --import >$GODOT_LOG 2>&1 </dev/null" \
    >/dev/null 2>&1
  sshrun "export XDG_RUNTIME_DIR=/run/user/\$(id -u); \
          WD=\$(ls \$XDG_RUNTIME_DIR | grep -m1 '^wayland-'); \
          export WAYLAND_DISPLAY=\${WD:-wayland-0}; \
          export DOTNET_ROOT=/usr/lib/dotnet; \
          export PATH=\"\$PATH:/usr/lib/dotnet:/opt/dotnet\"; \
          export HC_CLASS='$HC_CLASS'; \
          nohup /usr/local/bin/godot --editor --path $GAME_DIR >$GODOT_LOG 2>&1 </dev/null &" >/dev/null 2>&1
  UP=0
  for _ in $(seq 1 12); do sleep 2; port_up 6550 && { UP=1; break; }; done
  if [ "$UP" != 1 ]; then
    echo "GODOT_START_FAILED (log tail:)"; sshrun "tail -15 $GODOT_LOG" 2>/dev/null; exit 5
  fi
  echo "OK"
else
  echo "GODOT_RUNNING"
fi
exit 0
