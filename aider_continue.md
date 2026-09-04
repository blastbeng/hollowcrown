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
1. Balance harness v1 (NEXT TASKS 3 pulled up): it UNBLOCKS the last two
   revenant verifications — ward ABSORPTION (a hit on a warded body) and the
   uncapped LEECH (a damaged caster) are unreachable offline (self-hit
   guard) and need a bot attacker. Bot = headless client that joins,
   moves toward the nearest target and chain-attacks on a timer (no MCP
   dependency, launch flag --bot). Then print the winrate matrix per
   BALANCE.md. ALSO add HC_JOIN env support in Main (mirror of HC_CLASS)
   so the playtester client can join realms for driven PvP tests.
2. Arena polish remainder: gothic arches (store assets or Blender), banner
   sway, chains/cobwebs (6.7), ember mote tuning (currently reads as glow —
   want distinct rising sparks). (Rubble ~2x DONE, verified on screen.)
3. XP/leveling + progression sync to central + results screen; while there:
   character-select card click should set PlayerController.PendingClass +
   CombatAuthority.PendingClass (classes are boot-flag only right now).
4. Loot: procedural items/affixes + inventory/equip UI + visual tint.
5. MMR/Elo reporting + leaderboard UI + tiers (central endpoints still open;
   Vision 4 wants match-server tokens for /servers/heartbeat + PUT progress
   — land them here).
6. Skirmish mode (3v3) + team spawns/score.
7. Open world zone: village chunks, shrines, roaming elites, minimap.
8. Matchmaking quick-play flow via central.
9. Atmosphere pass 2: ambience audio, fog drift, fireflies.
10. Windows + Linux export presets + dedicated server headless export.
11. Robustness: rejoin UX (kicked/lost peers currently just resume offline —
    session 9 also saw a client ENet peer go INACTIVE while Networked==true,
    spewing "multiplayer instance isn't currently active" each frame: detect
    and recover), position-report trust checks (anti-cheat: shadow step is
    client-simulated movement, position reports unvalidated), nameplate HP
    bars over REMOTE avatars, attack-cast relay (puppets can't show remote
    swings/casts yet — only locomotion/hit/death), root/stealth visuals on
    remote puppets (root is server-applied but only the LOCAL body locks;
    RemoteAvatar.OnRooted is a no-op stub).

SESSION 10 NOTE (2026-09-04) — NIGHTBLADE + REVENANT SLICE 1 DONE, all
verified end-to-end (Vision 7 + 6.8). BOTH classes playable end-to-end with
the rigged model pipeline: ClassVariant export on WardenModel (tint + weapon
sockets + attack clips per class; nightblade = twin 0.35m daggers + dark
leather tint at BodyScale 1.12 = exactly the 1.8m spec; revenant = staff +
gem + hood cowl, dark robe arcane cast). NIGHTBLADE: fast 14/14/28 stab
chain (Punch clips, 100deg x 2.0m), shadow step (E — 6m blink toward cursor,
raycast shortens at world geometry but BLINKS THROUGH combat bodies),
stealth (R — 5s server-owned ghost, breaks on attack, next hit x1.50
server-computed), smoke bomb (F — 3.5m/6s blind zone thrown to cursor: hits
out of/through the cloud REJECTED server-side, victims' clients get a
screen-dark blind overlay). REVENANT slice 1: bone spear (Q — 18 dmg,
9m x 1.2m ground LINE hitbox, server validates lateral offset, bone bolt
visual travels the line), grave grasp (E — 4.5m cursor circle, 6 dmg,
1s server-applied ROOT: rooted bodies cannot move/dodge but CAN still
fight, expires on the timer). ICombatTarget grew OnStealthed/OnRooted;
Attack record grew Shape(Arc/Line)/Width/RootSeconds (CombatTables = the
single number source, BALANCE.md updated per class). HUD ability bar is now
CLASS-AGNOSTIC: each chain/kit implements IAbilityProvider, ArenaHud walks
the player's children (Nightblade: Q Daggers/E Step/R Stealth/F Smoke;
Revenant: Q Spear/E Grasp). Class flows through the realm handshake
(PendingClass + HandshakeRpc 2-arg + SpawnPlayerRpc 4-arg): server names
peers Nightblade#/Revenant#, enemy puppets render the right variant, smoke
zones broadcast identically to every peer (SmokeZone node in group
"smoke_zone", self-expiring).
EVIDENCE (all server-authoritative lines from the realm log): nightblade
chain 100->86->72 exact (attack=5 dmg=14, attack=6 dmg=14), stealth grant
then breaking hit dmg=42 hp=30 mult=1.50 (attack=7); blind: "AUTHORITY
REJECT ... attacker smoke-blind" (zone at the caster) AND "victim
smoke-blind" (input-driven stab through the cloud); shadow step "STEP 6,0m
toward cursor"; revenant spear in-line 100->82 exact (attack=8 dmg=18) +
"AUTHORITY REJECT ... outside line (lateral 3,00 > 0,95)"; grave grasp
82->76 (attack=9 dmg=6) + root: held move_forward 1s while rooted = ZERO
displacement, rooted spear still applied 18 dmg, root expires on the timer
(rooted=false after 3s); multiplayer smoke headless --server + client:
"approved (nightblade/revenant)", "spawned Nightblade#/Revenant#<peer>",
zero errors/screenshots judged vs Section 6 (dark leather twin-dagger
silhouette, hooded staff silhouette, smoke cloud + ground disc + blind
overlay all read at iso zoom).
COMMITS c31a65a..HEAD. FIXES found by testing: (a) retint override was
silently overwritten by the plain fallback after the switch — the body had
ALWAYS rendered default-white (the steel shader was dead code); per-mesh
single assignment now (warden plate + nightblade leather + revenant robe
all verified on screen). (b) shadow-step ray hit combat bodies (dummy
blocked the blink at 0.8m) — combat bodies excluded, world geometry still
stops the blink. (c) --class never reached the handshake
(CombatAuthority.PendingClass static was unwired) + PeerInfo.ClassId kept
its default when the connect event won the race — server named nightblade
peers Warden; both fixed, smoke-tested. (d) Godot 4.7 Variant.As<T> THROWS
on mismatch: the occlusion ray ended inside the player's own collider and
spammed InvalidCastException every frame — exclude the followed body's RID
+ pattern-match collider.Obj. (e) root movement gate was lost in a failed
edit batch (player walked while "rooted") — re-applied and re-verified.
GOTCHAS (session 10): (27) C# statics are NOT accessible from GDScript
(load().PendingClass fails) — class selection for playtester runs goes
through HC_CLASS env: remote_test.sh exports it before launching the
editor, the game inherits it (Main reads it as the --class fallback).
(28) kit cooldowns tick in GAME time: frozen tool-call latency does NOT
consume them but wall-clock zone/stealth/respawn timers DO (Time.GetTicksMsec
runs during freezes — an expiring smoke zone will be gone between two exec
calls; compress throw+probe into one godot_game_time step). (29) C# method
names stay PascalCase in exec GDScript (zone.Contains, not contains);
C# static fields are invisible to GDScript property access. (30) The
bridge is single-client: duplicate godot-mcp node processes (stale npx
children) hold the slot — kill the stale pid holding the ESTAB 6550
connection, or restart the editor. (31) The playtester runs the game
WITHOUT user args — HC_CLASS is the only clean way to pick the class for
MCP-driven runs. NEXT: revenant slice 2 (drain + ward). — DONE in the same
session (see session 10 addendum below).

SESSION 10 ADDENDUM — REVENANT SLICE 2 DONE and verified (Vision 7):
life drain (R — 2s channel, 4 x 8 dmg ticks along a 6 m x 1.2 m line,
server leeches 50% of each tick to the caster via HealedRpc, capped at max
HP) + soul ward (F — server-owned 40 absorb pool eaten BEFORE HP, 8 s
expiry, WardStateRpc mirror + arcane disc under the caster). ICombatTarget
+= OnWard/OnHealed. EVIDENCE: drain 100->92->84->76->68 EXACT (attack=10
dmg=8 x4), line VFX verified numerically (mesh AABB far end = dummy
direction), stamina cost, ward grant "absorbs 40 for 8s" + arcane disc on
screen + "ward expired peer=1" on schedule, stun breaks the channel (no
ticks after OnStunned), leech capped branch proven by ABSENCE of heal lines
at full HP. ABSORPTION + uncapped leech are UNREACHABLE offline (self-hit
guard victimId == attackerPeer is correct game behavior) — they need a bot
attacker: balance harness (now NEXT TASKS 1) unblocks them. ALL FOUR
revenant/niginblade HUD slots render (Q Spear / E Grasp / R Drain / F
Ward). Commits through 3895899.

SESSION 9 NOTE (2026-09-04) — RIGGED CLASS MODELS DONE and verified
end-to-end (Vision 6.8; capsule stand-ins retired on BOTH local player and
remote avatars). Asset: "Quaternius IK-Rigged Characters" (old library #5235,
CC0 — verified LICENSE file; JamesonBradfield/Quaternius) = rigged
GeneralSkeleton humanoid (65 bones, FACE meshes: eyes + eyebrows) + full
UAL1_Standard animation library. LAYOUT GOTCHA: the pack's mesh .res files
embed material refs to res://Godot - UE/*.png — the textures MUST sit at
game/Godot - UE/ (author's original layout) or every load errors; lean-copied
male-only + Animations/*.res (glb.import save_to_file uids reference them —
omitting them sprays 91 'Unrecognized UID' warnings). New: WardenModel.cs
(shader-retinted steel body, sword+shield BoneAttachment3D sockets on
RightHand/LeftHand, locomotion from velocity, one-shot attack/roll/hit/death
API, EnemyTint export) + steel_limb.gdshader (grayscale-luminance body tint —
a plain AlbedoColor multiply CANNOT desaturate the warm tan suit; judge on
screen, two failed tints preceded the shader). PlayerController drives the
model (WASD=Walk/Jog, Space=Roll, chain=Sword_Attack via WardenChain hook,
hits=Hit_Chest, death=Death01+fade). RemoteAvatar: same model, cold-steel
EnemyTint, red nameplate, locomotion from relay velocity, hit/death mirrors.
SCALE proof: head bone 1.61m natural -> scale 1.15 => 1.84m (6.4 spec 1.8m);
greatsword 1.44m. EVIDENCE: dummy chain 100->80->25 EXACT via the authority
with Sword_Attack raised-blade frames + ground arc flash; PvP 2 clients+server:
server log "hit victim=... attack=1/2/3 dmg=20/20/35 hp=80/60/25", target
frame WARDEN#<peer> live, red-nameplate enemy puppet on screen; dodge stamina
100->75 exact; fixed REALM JOIN RACE (JoinRealm dialed BEFORE attaching
CombatAuthority -> ConnectedToServer fired into the void -> server held the
join unapproved forever; now authority attaches first + _Ready catch-up
handshake; server log "peer approved — spawns at ..." + position reports
flowing). FIXED --join crash (EnterRealm dereferenced _ui before menus were
built — caught by the B-client smoke test). Commits a1b13fc..96f5c1a.
GOTCHAS (session 9): (22) type_text/inputs are DROPPED while the game is
frozen — thaw first, or ride inputs inside godot_game_time step. (23) After
teleporting the player, the iso camera needs ~0.5s thawed to converge before
a screenshot. (24) RemoteAvatar node name is "Remote{peerId}", not
"RemoteAvatar". (25) EnterRealm is now called before dialing: it must never
assume _ui exists (--join path). (26) Two Client B relaunches needed the
freshest assembly — kill the old --join process before relaunch after a pull.

SESSION 8 NOTE (2026-09-04) — FULL REPO REVIEW done (verdict: first steps
sound — architecture, authority model, security basics and tooling all match
the spec). Review fixes committed f795265: CentralClient no longer leaks
network exceptions as unobserved task faults (UI "central unreachable?"
statuses fire now); central heartbeat field-length caps + expired-token
purge on auth; ArenaHud player HP row (server-mirrored); PlayerController
_mcp_state exposes hp/dead/stunned/peer_id. REVIEW DEFERRALS (known, not
bugs-now): (a) /servers/heartbeat is unauthenticated and PUT progress only
needs a user token — Vision 4 wants match-server tokens; land them with the
MMR endpoints (NEXT TASKS 7). (b) Kick/disconnect UX: a wrong-password kick
or server drop silently resumes offline mode with the arena loaded — UI
notification + return-to-menu is NEXT TASKS 13. (c) --join failure with a
dead host leaves a black screen (Main never built the menu UI) — fold the
fix into the rejoin task. (d) Central has no rate limiting and tokens are
stored plaintext in SQLite (fine for v1; harden before any public host).

SESSION 7 NOTE (2026-09-04) — REALM HANDSHAKE + NETWORKED PLAYERS DONE and
verified end-to-end (Vision 4 + 9). New: password handshake RPC right after
ENet connect (wrong password = SceneMultiplayer.DisconnectPeer kick; client
resumes offline mode on ServerDisconnected); deterministic duel spawn points
(SpawnPoints, catch-up replay of the roster to late joiners); SpawnPlayer/
DespawnPlayer broadcasts; players are ICombatTargets (CombatId == ENet peer
id, boot-time id 1 re-registered on approval); 10 Hz client->server position
reports relayed to peers; RemoteAvatar puppets (cold steel + red enemy
nameplate, lerp, hit punch, fall/fade death, respawn teleport); PvP hits
validated server-side vs the victim's REPORTED position (self-targeting
rejected); player death/respawn/stun mirrors in PlayerController (fall with
0.35 m lift so the lying capsule rests on the floor); kits target group
"combat_targets" (dummies + players, self excluded); HUD target frame
generalized (enemy name + mirrored HP). Main.JoinRealm(host, port, password)
+ --join host:port [--password x] launch flag (direct IP join + automated
second client). EVIDENCE (server + 2 clients): kick logged on wrong password;
roster catch-up gave client 1 client 2's avatar; server beat showed reported
positions moving; full PvP kill — chain 20/20/35 + bash 15 = exact BALANCE
numbers, killfeed "Warden#X slew Warden#Y" on screen, victim client logged
DOWN/STUNNED/RESPAWNED, avatar fell + faded, respawn at spawn point; self-hit
rejected. Commits 47d3bae..8575ad6 + 95cf335 (recovered 18 .uid files from
the mirror's auto-checkpoints; pull.rebase set on the mirror).
GOTCHAS (session 7): (17) pkill -f PATTERN self-matches the SSH bash -c
command line whenever the pattern text appears anywhere in the same command
string — put the kill in its OWN ssh call with a bracketed pattern
(hea[d]less), never combine with relaunches. (18) The frozen OS cursor's
ground point MOVES WITH THE CAMERA: when parking the player for a cursor-aim
test, compute facing = player->cursor ground point first and park the enemy
in THAT arc (the offset is a fixed world direction, ~+Z here). (19) On
clients, CombatAuthority's _hp digest does NOT update from ApplyHitRpc — read
the target's own mirror (RemoteAvatar.Hp / target frame) instead; server-side
_hp is the truth. (20) Headless server/client processes keep running an OLD
assembly until relaunched after a pull — relaunch them after every pull
(stale-server cost us a debug round). (21) Server respawn (wall clock)
routinely beats a screenshot/read round trip — a 100 HP read right after a
kill usually means the respawn already happened.

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
