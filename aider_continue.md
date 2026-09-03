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
1. Realm handshake after ENet connect (Vision 4): password check + spawn flow
   + peer names — join now loads the arena (RealmJoined -> Main.EnterRealm)
   but there are no networked PLAYERS yet. Fold in: spawn both peers' wardens,
   player HP/death/respawn through CombatAuthority (ICombatTarget), enemy
   nameplates red (6.8), position sync, PvP hit flow (client requests vs
   remote player). (Session 3 note resolved: arena now loads on connect.)
2. Rigged class models: store humanoid with FACE + per-class silhouette
   (Warden broad + shield), retint, weapon sockets, run/attack/roll/death
   anims (Vision 6.8 — capsule stand-in is temporary).
3. Nightblade + Revenant kits (data-driven, BALANCE.md entries).
4. Arena polish remainder: gothic arches (store assets or Blender), rubble
   stones scale up ~2x, banner sway, chains/cobwebs (6.7), ember mote tuning
   (currently reads as glow — want distinct rising sparks).
5. Balance harness v1: bot mirror matches, winrate matrix printed.
6. XP/leveling + progression sync to central + results screen.
7. Loot: procedural items/affixes + inventory/equip UI + visual tint.
8. MMR/Elo reporting + leaderboard UI + tiers (central endpoints still open).
9. Skirmish mode (3v3) + team spawns/score.
10. Open world zone: village chunks, shrines, roaming elites, minimap.
11. Matchmaking quick-play flow via central.
12. Atmosphere pass 2: ambience audio, fog drift, fireflies.
13. Windows + Linux export presets + dedicated server headless export.
14. Robustness: disconnects, rejoin, XP/MMR validation caps.

SESSION 6 NOTE (2026-09-04) — COMBAT SERVER AUTHORITY DONE and verified
end-to-end (Vision 2.3). New: CombatAuthority (/root/Main/CombatAuthority on
BOTH peers, force_readable_name) owns every HP number: ENet RPCs SubmitHit/
SubmitBuff (AnyPeer client->server) + ApplyHit/TargetStunned/TargetRespawned/
KillFeed (Authority broadcast, CallLocal=true); server validates range/arc/
per-peer cooldown against ITS own world; CombatTables.cs is the single
server-owned number source (chain 20/20/35, bash 15+0.5s stun, buff cap 1.25,
10s); TrainingDummy is a pure ICombatTarget mirror (fall/respawn server-
driven); dedicated server + client both host the arena; ServerBrowser.RealmJoined
-> Main.EnterRealm loads it on connect. EVIDENCE: offline validation matrix
(valid 100->80; range/arc/cooldown/unknown-id all REJECTed, hp untouched);
input path R->Q->E = exactly 23+17 server dmg (buff capped/expired server-side:
later hits 20/15); offline kill -> killfeed -> respawn at 3s; MULTIPLAYER smoke:
headless --server 7799 + client (id 313781323) fights over ENet — server log
trail hp 80->60->25->5->0 + respawn, client mirror matches, cooldown AND
range (30.00 > 2.75) cheats rejected server-side, killfeed "Warden#<id> slew
the Training Dummy" on screen, screenshot evidence vs Section 6 (target frame
live server HP, bash cone + stun ring flat on floor, damage numbers, cooldown
sweeps, stamina costs). Commit 4d98e99.
GOTCHAS (session 6): (13) exec GDScript vs C#: NATIVE props snake_case
(name, visible, current_scene); C#-declared PRIVATE fields are unreachable —
read via public getters or walk the tree for Label.text; if/for need colons.
(14) An identical repeated exec call is blocked by a dedupe guard, but an
earlier "duplicate" may still have executed — always re-read state instead of
assuming (a phantom extra hit proved this). (15) OfflineMultiplayerPeer is
ALWAYS set -> HasMultiplayerPeer() is true offline; gate networked on
`MultiplayerPeer is not OfflineMultiplayerPeer` (CombatAuthority.Networked).
Time.GetTicksMsec runs on WALL CLOCK even frozen — respawn/cooldown timers
elapse between tool calls (server respawn beats screenshots; reads may show
post-respawn 100/100). (16) `ss -tln` is TCP-only — ENet is UDP (`ss -uln`);
verify the dedicated server via its log lines, not port probes.

SESSION 5 NOTE (2026-09-03) — Warden kit COMPLETE and verified end-to-end
(vision 7): shield bash E (90deg x 3.2m cone flash, 15 dmg, 0.5s stun with
bone ring marker), warcry R (+15% chain dmg 10s, 8m accent ring), shield
wall F (100% block 2s, 25 stamina/s drain, steel disc, ends at empty).
Combat math proven: dummy 100 -> 62 after bash+buffed chain (15 + 20*1.15 =
38). Kill -> fall -> 3s respawn verified. Arena HUD DONE (vision 6.10):
bottom-center Q/E/R/F ability bar with cooldown sweeps, stamina bar + number,
top-center target frame with live dummy HP — all real state. Atmosphere
particles DONE (6.2/6.7): ember motes in braziers (add-blend), slanted rain.
Screenshots judged vs Section 6: telegraphs flat/palette-correct, HUD gate
now passes in the arena scene.
GOTCHAS (session 5): (7) bash/chain aim at the OS cursor ground point — the
remote cursor sits at window (0,0); to hit a target place the PLAYER on the
far side of the target from the cursor point (read it via
project_ray_origin/normal of get_mouse_position, then position at
target + dir*2). (8) exec GDScript: no `new Vector3` (Vector3() only);
C# NATIVE properties need snake_case (global_position, rotation_degrees,
velocity); C#-declared props keep PascalCase (Hp, Stamina). (9)
godot_editor_edit restart did NOT restart the editor (check_stale stayed
stale) — kill the editor via SSH then `bash tools/remote_test.sh --restart`,
which works. (10) ParticleProcessMaterial.ColorRamp wants a
GradientTexture1D, not Gradient. (11) target-typed `new(...)` fails inside
operator expressions (CS8310) — write `new Vector3(...)`. (12) An old
orphaned game window from a prior session can hold the display — kill stray
godot processes before restarting the editor.
SESSION 4 NOTE — isometric camera rig DONE and verified end-to-end
(camera_test.tscn via playtester scene_path): ortho yaw 45 / pitch -50 /
size 12 proven numerically; smooth-follow converged onto a teleport; cursor
ground-aim reticle EXACT (delta 0 vs ray-plane math); occlusion fade
triggers (player visible through 4m wall) and restores to opaque; zoom
12->13 per wheel step, clamps 8/18 verified via direct handler calls.
Duel arena v1 dressing DONE (ring wall + breach, obelisk, braziers, seeded
MultiMesh rubble), zero script errors, screenshot gate passed.
GOTCHAS (session 4): (1) NoiseTexture2D grayscale mean ~0.5 halves every
AlbedoColor — double tints. (2) MeshInstance offset from body origin MUST be
mirrored on CollisionShape3D (half-buried collider killed the fade ray).
(3) Synthetic mouse wheel (parse_input_event / push_input /
push_unhandled_input) never reaches C# _UnhandledInput in the bridge —
verify via rig._unhandled_input(ev); real OS wheel unaffected. (4)
Input.warp_mouse is a no-op on the remote (Wayland). (5) Runtime-added
nodes get @Class@N names — find by type, not name. (6) exec GDScript: no
parens on for-loops, Vector3() not new Vector3().
