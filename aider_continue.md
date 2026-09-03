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
- If the playtester MCP still cannot connect while godot IS online and
  listening on the correct port (6550), AiderDesk may be holding an old MCP
  session: fix with `sudo systemctl restart aiderdesk`, then retry the MCP
  call. Also kill orphaned godot-mcp node processes older than the current
  session (they hold the single-client bridge slot).
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

## 4b. MCP TOOL MAP (verified live in session 1 — use EXACT names)
- playtester (remote 192.168.1.29:6550; local mirror = same names with
  godot-playtester-mcp-local prefix): run/stop/restart/rescan:
  godot_editor_edit (action=run, frozen=true | stop | restart | rescan) |
  screenshot: godot_editor_read (action=screenshot_game / screenshot_editor) |
  logs/errors: godot_editor_read (action=get_log_messages / get_stack_trace) |
  input: godot_input (action=sequence / type_text / get_map) |
  state: godot_runtime_state (digest / watch_start / watch_collect) |
  time control: godot_game_time (freeze / step / step_until / thaw) |
  exec GDScript: godot_exec (action=run) | scene/nodes: godot_scene,
  godot_node_read, godot_node_edit | project: godot_project (get_info /
  get_settings) | 3D: godot_scene3d | resources: godot_resource |
  animations: godot_animation_read/edit | tilemap/gridmap: godot_tilemap_read,
  godot_tilemap_edit, godot_gridmap_read, godot_gridmap_edit |
  profile: godot_profiler | mesh check: godot_validate_meshes |
  docs: godot_docs (fetch_class / fetch_page)
- playtester INPUT CAVEATS (session 2): type_text is the ONLY way to fill
  LineEdits (raw per-key injection sends no unicode: nothing is typed, but
  Enter keys still fire focus-walk/submit). After a screen swap the new
  screen must GrabFocus, or type_text goes into the void. After pushing
  commits you MUST run tools/remote_test.sh (it pulls); stop+run alone
  rebuilds only what is already on the remote disk. If the bridge says
  "closed client" while godot listens on 6550: sudo systemctl restart
  aiderdesk (restarts this agent too; check uptime, then just retry).
- Godot C# LAYOUT CAVEAT (session 2): SetAnchorsPreset(FullRect) preserves
  the control's current size (offsets become negative) — a fresh Control
  stays 0x0. Use SetAnchorsAndOffsetsPreset. C# lambda trap: naming a
  lambda parameter "_" makes `_ = Task` assign to the parameter, not discard.
- godot store (old library): search: godot-store-mcp---library_search
  (categories via godot-store-mcp---library_configure, details via
  godot-store-mcp---library_get_asset) | install:
  godot-store-mcp---library_download_asset (downloads zip locally).
  New store: search godot-store-mcp---store_search, details
  godot-store-mcp---store_get_asset, download godot-store-mcp---store_download_asset.
- blender: NOT configured (no blender MCP in mcp-servers.json) -> fallback to
  store assets / primitives per project_vision.md Section 5, note it in commits.
- web search: gateway---searxng_web_search | page read: gateway---web_url_read,
  power---fetch | playwright: playwright---browser_navigate, browser_snapshot,
  browser_take_screenshot | github search: gateway---search_code,
  gateway---search_repositories, gateway---search_issues,
  gateway---search_pull_requests, gateway---search_commits
  (NOTE session 1: gateway GitHub auth kept rotating device codes — if
  unavailable, use local git + web search until it is fixed).

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
1. Dedicated server mode: --server flags, heartbeat to central, headless boot test green.
   NOTE (session 3): server browser UI is DONE and playtested (list from central with
   empty-state, PW badge + password dialog, direct IP join verified END-TO-END against a
   real ENet listener on :7799 — server logged PEER_CONNECTED; Back/Refresh/mode filter
   work; zero console errors). The chosen password is captured in the UI but only travels
   with the realm handshake once the dedicated server exists — wire it then. Test listener
   script pattern: SceneTree._process + get_multiplayer() (bare `multiplayer` does not
   parse in -s scripts; pkill -f patterns must use [b]racket trick or they kill the ssh
   shell).
3. Isometric camera rig (Vision 6.1): locked yaw 45 / pitch -50, orthogonal, zoom,
   smooth follow + cursor ground-aim (Aim) + reticle decal + occlusion fade shader.
   Screenshot: camera behind wall must NOT hide the player.
4. Duel arena v1: seeded procedural arena (Vision 6.6) + WorldEnvironment (Vision 6.2).
5. Player controller: WASD camera-relative movement, sprint, dodge roll,
   footstep/roll animation hooks.
6. Combat core: ground-projected hitboxes, telegraph decals, damage numbers,
   death/respawn, killfeed; Warden kit complete (chain, bash, warcry, wall).
7. Nightblade + Revenant kits (data-driven, BALANCE.md entries).
8. Balance harness v1: bot mirror matches, winrate matrix printed.
9. XP/leveling + progression sync to central + results screen.
10. Loot: procedural items/affixes + inventory/equip UI + visual tint.
11. MMR/Elo reporting + leaderboard UI + tiers (central endpoints still open).
12. Skirmish mode (3v3) + team spawns/score.
13. Open world zone: village chunks, shrines, roaming elites, minimap.
14. Matchmaking quick-play flow via central.
15. Atmosphere pass 2: rain, embers, banner sway, ambience audio.
16. Windows + Linux export presets + dedicated server headless export.
17. Robustness: disconnects, rejoin, XP/MMR validation caps.
