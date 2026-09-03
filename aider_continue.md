# AIDER CONTINUE — OPERATING PROCEDURE (read fully every session, follow exactly)

## 0. ALWAYS
- Read project_vision.md first, then this file, then NEXT TASKS below.
- Game: HOLLOWCROWN, dark fantasy ISOMETRIC PvP MMO (Diablo 2 / V Rising
  controls). Language: C# on Godot 4 (.NET), plus a C# ASP.NET central
  server. GDScript is forbidden. Hollowcrown.sln at repo root:
  game/ (Godot) + central/ (central server) + shared/ (DTOs).
- Local repo = /opt/projects/hollowcrown. Remote mirror = /opt/projects/hollowcrown
  on Ubuntu PC 192.168.1.29 (synced via git: local push -> remote pull).
- The remote PC is NOT always online. Detect first, never assume.
- SSH KEY LOGIN IS THE ONLY ACCESS METHOD. If ssh answers "Permission denied
  (publickey)", the PC is up but booted into another distro/OS that does not
  accept our keys: treat it as HOST_OFFLINE (offline fallback), retry next
  iteration. NEVER ask the user to change or re-add ssh keys — just continue
  locally and use the remote only when the key login is accepted again.
- If the Godot editor is closed on the remote host, open it over SSH
  (remote_test.sh launches it automatically; manual command in Section 4).
  Check port 6550 first — never assume the editor is running.
- When unsure, prefer the action that produces testable evidence.

## 1. REMOTE TEST ENVIRONMENT (primary)
- The godot-playtester MCP connects to the Godot instance on the remote
  Ubuntu PC at 192.168.1.29:6550. Its tools: run/stop project, scene tree,
  screenshots, logs/errors, input. Use exact tool names from the MCP client;
  if a tool list is pasted in Section 4b, use those names.
- The Godot store MCP and Blender MCP run in the local workspace (see
  project_vision.md Section 5 for when to use each).
- SSH: ssh -i ~/.ssh/id_ed25519 blast@192.168.1.29 (Wayland desktop).
- Repo: /opt/projects/hollowcrown. Godot binary: /usr/local/bin/godot (must
  be the .NET build — verify once: version string contains "mono" or ".net").
  dotnet SDK 8+ required on BOTH machines.
- ONE command does everything: bash tools/remote_test.sh
  (host check -> clone-or-pull with auto-commit if the remote tree is dirty
  -> start central server on :6560 if down -> launch the editor on Wayland
  if port 6550 is down -> prints a status token).
  Tokens: OK | GODOT_RUNNING | HOST_OFFLINE | SYNC_FAILED | GODOT_START_FAILED
  | CENTRAL_FAILED
- Use bash tools/remote_test.sh --restart when the remote editor does not
  reflect newly pulled code.

## 2. ITERATION LOOP (every iteration, exactly)
1. IMPLEMENT one small complete change (one feature / one fix / one visual
   upgrade). Use store MCP for assets, Blender MCP for custom meshes (when
   available), web/GitHub search for API/reference checks.
2. VERIFY COMPILE: dotnet build Hollowcrown.sln (must pass before commit).
3. COMMIT + PUSH: git add -A && git commit -m "<what and why>" && git push
4. RUN: bash tools/remote_test.sh
5. If OK or GODOT_RUNNING -> use the godot-playtester MCP tools to:
   a. run the project / current scene;
   b. read errors and console output;
   c. SCREENSHOTS: menu, character screen, WIDE isometric arena shot,
      combat action shot (telegraphs + reticle + HUD visible);
   d. interact: move (WASD), cursor-aim a skill, dodge, block/parry, attack,
      kill, respawn; verify occlusion fade when behind walls;
   e. multiplayer smoke test if networking changed: dedicated server
      (headless --server) + client or bots connect and fight;
   f. if progression changed: login -> create/modify character -> save ->
      reload -> verify data.
6. JUDGE the screenshots against project_vision.md Section 6. Gray boxes,
   empty arenas, missing UI, floating props, faceless characters, camera
   clipping through walls = fix NOW, or make it the top task of the next
   iteration.
7. HOST_OFFLINE -> skip the playtester this iteration: run
   bash tools/test.sh (dotnet build + local headless checks + central server
   boot check), review your diff, commit with "(remote host offline,
   untested)" in the message. Retry remote next iteration.
8. SYNC_FAILED, GODOT_START_FAILED or CENTRAL_FAILED -> print the script's
   error output, retry once, then use the offline fallback and note it.

## 3. MCP-EDIT SYNC RULE
If the playtester MCP or remote editor was used to modify scenes/assets, the
REMOTE tree now has uncommitted changes. Before the next local push:
- ssh -i ~/.ssh/id_ed25519 blast@192.168.1.29 'cd /opt/projects/Hollowcrown && git add -A && git commit -m "remote: MCP edits" && git push'
- locally: git pull --rebase
Blender MCP and local asset edits happen locally: just commit + push them.
Never let the remote and local trees diverge silently.

## 4. MANUAL COMMANDS (only if remote_test.sh is missing or broken)
host check:
  ssh -i ~/.ssh/id_ed25519 -o ConnectTimeout=6 blast@192.168.1.29 'echo online'
pull:
  ssh -i ~/.ssh/id_ed25519 blast@192.168.1.29 'cd /opt/projects/Hollowcrown && git pull'
if pull fails (dirty tree):
  ssh -i ~/.ssh/id_ed25519 blast@192.168.1.29 'cd /opt/projects/Hollowcrown && git add -A && git commit -m "auto: checkpoint before sync"; git pull'
still stuck: append ' && git pull --rebase' to the last command.
central server check/start:
  ssh -i ~/.ssh/id_ed25519 blast@192.168.1.29 'ss -tln | grep 6560'
  ssh -i ~/.ssh/id_ed25519 blast@192.168.1.29 'cd /opt/projects/Hollowcrown && ASPNETCORE_URLS=http://0.0.0.0:6560 nohup dotnet run --project central >/tmp/hollowcrown_central.log 2>&1 &'
playtester port check:
  ssh -i ~/.ssh/id_ed25519 blast@192.168.1.29 'ss -tln | grep 6550'
start godot editor (Wayland):
  ssh -i ~/.ssh/id_ed25519 blast@192.168.1.29 'export XDG_RUNTIME_DIR=/run/user/$(id -u); WD=$(ls $XDG_RUNTIME_DIR | grep -m1 ^wayland-); export WAYLAND_DISPLAY=${WD:-wayland-0}; nohup /usr/local/bin/godot --path /opt/projects/Hollowcrown/game >/tmp/hollowcrown_godot.log 2>&1 &'
  wait 10 s, recheck the port, else read /tmp/hollowcrown_godot.log.
headless dedicated server smoke test:
  ssh -i ~/.ssh/id_ed25519 blast@192.168.1.29 'cd /opt/projects/Hollowcrown/game && timeout 15 /usr/local/bin/godot --headless -- --server --port 7799 2>&1 | tail -20'
compile check (remote or local):
  dotnet build Hollowcrown.sln

## 4b. MCP TOOL MAP (fill in after first session — use EXACT names)
- playtester: run/stop: <names> | screenshot: <name> | logs: <name> | input: <name>
- godot store: search: <name> | install: <name>
- blender: probe: <name> | export: <name>
- web search / playwright / github search: <names>

## 5. SESSION FLOW
1. Read project_vision.md + this file + NEXT TASKS.
2. Quick state only: git log --oneline -15. Open only the files you will touch.
3. Take the TOP task of NEXT TASKS (or the worst problem visible in the last
   screenshots).
4. Run the Section 2 loop. Repeat for as many iterations as the session allows.
5. Stop only when the task is done, tested and pushed — or when blocked; then
   say exactly WHAT is blocked and WHY.
6. Session end: UPDATE NEXT TASKS (remove completed, add new problems on top).
7. Short summary: what changed, test evidence (screenshots/errors), next task.

## 6. RULES
- Small steps. One coherent change per iteration. No giant rewrites.
- Never rewrite a working system without a concrete reason.
- Missing file? ASK the user by name. Never invent file contents.
- No claims without evidence (screenshot or clean run).
- No TODO placeholders. Finish what you start, or revert it.
- Implementation over discussion. No long reports.
- Autonomy: keep working down the task list until the user says stop.
- .gitignore must contain: .godot/, bin/, obj/, *.user, *.tmp (dirty build
  output is the main cause of remote pull failures).

## 7. NEXT TASKS (top = next; rewrite this list as you work)
1. Bootstrap: Hollowcrown.sln (game + central + shared), minimal C# main
   scene, dotnet build green, Godot-MCP/playtester plugin installed and
   reachable on 192.168.1.29:6550, tools/remote_test.sh run green. Fill
   Section 4b tool map.
2. Central server v1: auth (register/login, salted hashes) + characters
   CRUD + SQLite + heartbeat registry. test.sh boots it and curl-checks.
3. Client login/register UI + token storage + character select/create.
4. Dedicated server mode: --server flags, heartbeat to central, headless
   boot test green.
5. Server browser UI: list from central, password prompt, direct IP join.
6. Isometric camera rig (Vision 6.1): locked yaw 45 / pitch -50, orthogonal,
   zoom, smooth follow + cursor ground-aim (Aim) + reticle decal + occlusion
   fade shader. Screenshot: camera behind wall must NOT hide the player.
7. Duel arena v1: seeded procedural arena (Vision 6.6) + WorldEnvironment
   (Vision 6.2).
8. Player controller: WASD camera-relative movement, sprint, dodge roll,
   footstep/roll animation hooks.
9. Combat core: ground-projected hitboxes, telegraph decals, damage numbers,
   death/respawn, killfeed; Warden kit complete (chain, bash, warcry, wall).
10. Nightblade + Revenant kits (data-driven, BALANCE.md entries).
11. Balance harness v1: bot mirror matches, winrate matrix printed.
12. XP/leveling + progression sync to central + results screen.
13. Loot: procedural items/affixes + inventory/equip UI + visual tint.
14. MMR/Elo reporting + leaderboard UI + tiers.
15. Skirmish mode (3v3) + team spawns/score.
16. Open world zone: village chunks, shrines, roaming elites, minimap.
17. Matchmaking quick-play flow via central.
18. Atmosphere pass 2: rain, embers, banner sway, ambience audio.
19. Windows + Linux export presets + dedicated server headless export.
20. Robustness: disconnects, rejoin, XP/MMR validation caps.
