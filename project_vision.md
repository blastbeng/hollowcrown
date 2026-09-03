# HOLLOWCROWN — SPEC v2 (dark fantasy isometric PvP MMO; overrides all older docs)

## 0. READ FIRST (every session)
1. LOOKS ARE THE GAME. Flat gray boxes, empty arenas, default materials =
   FAILED task. Grim dark-fantasy atmosphere is a core requirement, not polish.
2. ISOMETRIC. Fixed camera, ground telegraphs, silhouette-first characters.
   A free-rotating or first/third-person camera is a spec violation.
3. LANGUAGE: C# (.NET) on Godot 4.x. GDScript is FORBIDDEN. Every script is a
   `public partial class` in a file whose name equals the class name. The
   central server is also C#.
4. ASSETS: search/install via the Godot store MCP before hand-building
   anything. Only CC0 / CC-BY / MIT licenses. Log every install in
   ATTRIBUTION.md in the same commit. Blender MCP: use it whenever available.
5. USE ALL MCP TOOLS: playtester (remote testing), Godot store (assets),
   Blender (custom meshes), web search (verify APIs), GitHub search (license-
   safe reference patterns), Playwright (docs scraping). Timeboxed: one pass
   per need; browsing never consumes a whole iteration.
6. Every iteration: implement -> dotnet build -> commit/push -> test per
   aider_continue.md (remote playtester + screenshots) -> judge -> fix.
7. Never restart or redesign the project. Evolve existing code.
8. Windows + Linux desktop ONLY. No mobile targets anywhere.
9. PvP IS THE GAME. Every system must serve skill-based PvP.

## 1. GAME
HOLLOWCROWN — dark fantasy ISOMETRIC PvP MMO. Godot 4.x + C#/.NET.
- Grim atmospheric world: gothic ruins, perpetual dusk, fog, embers, rain.
- Isometric ARPG controls (reference feel: Diablo 2 / V Rising / League):
  - fixed camera: yaw 45 deg, pitch ~-50 deg, ORTHOGONAL projection,
    smooth-follow the player, mouse-wheel zoom (ortho size 8-18),
  - WASD movement relative to camera yaw, dodge roll on Space,
  - ALL skills aim at the ground point under the mouse cursor; reticle decal
    marks it; abilities on QWER / 1-4.
- Heavily PvP-focused MMO with persistence: characters, classes, XP,
  equipment, and rank persist across ALL servers.
- Host-your-own dedicated servers (Diablo 2 / DayZ / Rust model):
  - any player can host a match server from the client, OR run the same
    binary headless: `godot --headless -- --server --port 7777 --name "My Realm" [--password "secret"]`
  - servers optionally password-protected; private servers are listed with a
    lock icon; direct IP join is always available.
- A lightweight CENTRAL SERVER (we host) provides: accounts, character
  storage, server registry, matchmaking, and ranking/leaderboards.
- Modes: Duel (1v1), Skirmish (3v3, first to X kills), Open World (persistent
  contested zone with shrines and roaming elites).
- Procedural: seeded arena maps, procedural item affixes, procedural world
  variation. All deterministic from seeds.
- Classes v1 (each mechanically distinct, see Section 7): Warden
  (sword+shield), Nightblade (dual daggers), Revenant (dark sorcery). A 4th
  class (Bonecaller) comes later — design it data-driven from day one.
- Combat is ACTIVE and skill-based: dodge rolls with i-frames, block/parry
  timing, stamina, telegraphed abilities, GROUND-PROJECTED hitboxes
  (circles, lines, cones on the floor plane). NO tab-target, NO homing.

## 2. HARD RULES
1. C# only. File name == class name. `using Godot;` everywhere.
2. Signals: `[Signal] public delegate void DiedEventHandler();` +
   `EmitSignal(SignalName.Died);`. RPCs:
   `[Rpc(MatchMode = MultiplayerApi.RpcMode.AnyPeer)]` style Godot 4 API —
   verify exact signatures via web search before first use.
3. Authority: the MATCH SERVER is authoritative for combat; the CENTRAL
   SERVER is authoritative for accounts/progression/rank. Clients never
   trust themselves.
4. All automation scripts live in tools/ with .sh and .bat variants; scripts
   use exit codes and never fake success.
5. No TODO placeholders, no stubs. "Done" requires screenshot or clean-run
   evidence.
6. One small complete change per iteration. No giant rewrites.
7. After ANY C# edit: `dotnet build Hollowcrown.sln` must pass before
   committing. Never commit code that does not compile.

## 3. PRIORITY (choose next work in this order)
1. Camera rig + combat feel: iso camera, cursor aim, movement, dodge, block,
   parry, hit feedback.
2. One arena + one class fully playable end-to-end (host -> join -> fight ->
   death -> respawn).
3. Dedicated server mode + central server round-trip (login -> character ->
   match -> progression saved and reloaded on another server).
4. Remaining classes, modes, matchmaking, server browser polish.
5. Atmosphere and visual pass (Section 6) — interleaved, never postponed.
6. Ranking, loot, progression depth.
7. Robustness: disconnects, rejoin, validation.
8. Polish and extra content.
"Working but ugly" and "pretty but broken" are BOTH unfinished.

## 4. ARCHITECTURE (mandatory shape)
- One solution, three projects:
  - `game/` — the Godot project (Godot.NET.Sdk),
  - `central/` — ASP.NET minimal API + SQLite (central server),
  - `shared/` — DTOs and protocol models used by both.
- Game launch modes (same binary):
  - client (default), `--server` (dedicated, headless-friendly),
  - flags: `--port N`, `--name "Realm Name"`, `--password "x"`,
    `--max-players N`, `--central URL`.
- Central server: REST + JSON, token auth, SQLite file DB, schema created/
  migrated in code at startup. Passwords stored as salted hashes, never
  plaintext. Runs at http://localhost:6560 by default (env
  `ASPNETCORE_URLS` / `--urls` can override).
- Client <-> match server: Godot high-level multiplayer (ENet), server-
  authoritative. Client <-> central: REST/JSON. Match server -> central:
  REST/JSON with a server token (heartbeats + progression reports).
- Central endpoints (minimum set):
| method + path                       | purpose                              |
|-------------------------------------|--------------------------------------|
| POST /auth/register {user, pass}    | -> {token}                           |
| POST /auth/login {user, pass}       | -> {token}                           |
| GET /characters (auth)              | list characters                      |
| POST /characters {name, classId}    | create character                     |
| GET /characters/{id}                | full snapshot (level, xp, gear, mmr) |
| PUT /characters/{id}/progress       | save progression (match server only) |
| GET /servers?mode=duel              | live server list                     |
| POST /servers/heartbeat             | register/refresh (TTL 30 s)          |
| POST /mmr/report {matchResult}      | Elo update (match server only)       |
| GET /leaderboard?mode=duel          | top 50                               |
- Config on client: central URL, saved token, settings -> JSON in user://.
- Anti-cheat stance: minimal but present — server validates XP/MMR reports
  against sane caps; deeper hardening is a LATER task, not now.

## 5. MCP TOOL POLICY
Paths: local repo /opt/projects/hollowcrown; remote mirror /opt/projects/hollowcrown on
Ubuntu PC 192.168.1.29 (git-synced: local push -> remote pull; local dotnet /opt/dotnet/dotnet,
local godot /opt/godot/bin/godot).
- godot-playtester (remote Godot at 192.168.1.29:6550): PRIMARY test loop.
  Always via `bash tools/remote_test.sh` first (it syncs git and launches the
  editor). If the host is offline, skip it and use local fallbacks.
- Godot store MCP: for every visual/audio need, FIRST search the store.
  Licenses CC0/CC-BY/MIT only (never GPL). One line per asset in
  ATTRIBUTION.md: name, author, license, URL. Retint assets toward the
  palette so art direction stays consistent. Commit asset files + .import
  metadata.
- Blender MCP: probe availability at session start (one cheap call).
  WHEN AVAILABLE you MUST use it for custom meshes the store lacks: statues,
  braziers, gates, broken walls, weapons, class armor pieces. Export .glb to
  `game/assets/models/`, commit, note "blender-mcp" in the commit message.
  WHEN UNAVAILABLE: fall back to store assets or primitives and say so in
  the commit message.
- Web search: verify Godot 4 API names/signatures for anything uncommon
  (versions drift — never trust memory for exact enum/API names).
- GitHub search: reference implementations (headless dedicated servers, Elo,
  procedural generation). Learn patterns; do NOT paste large code wholesale;
  prefer MIT/Apache sources; note inspiration in ATTRIBUTION.md.
- Playwright: scrape docs.godotengine.org or asset library pages when search
  results are insufficient.

## 6. VISUAL STANDARD (the most important section)

### 6.1 Isometric camera (defines the whole look — build FIRST)
- Rig: Node3D at the player, yaw LOCKED at 45 deg; child Camera3D with pitch
  -50 deg, Projection = Orthogonal, Size 12 (mouse-wheel zoom 8-18), far
  plane tight (~60 m). Smooth-follow with lerp. The camera NEVER free-
  rotates (optional 90-deg snap rotation is a later task).
- Aim: the ground plane (y=0) under the mouse cursor is the universal target.
  Cursor reticle: a flat ring decal mesh that follows the aim point.
- Level design for iso: flat arenas, elevation only via ramps and low
  platforms (<= 1.5 m), waist-high walls preferred, NO tall occluders
  between camera and player.
- Occlusion fade (MANDATORY): every frame, query from camera to player;
  any mesh hit gets a fade treatment (shader or transparency override to
  ~0.2 alpha) and restores when clear. Never let walls hide the player.
- Long dramatic shadows: DirectionalLight pitched ~-55 deg.
- Reference feel: Diablo 2 / V Rising / League of Legends.

### 6.2 Global atmosphere (do this once, early)
WorldEnvironment: overcast ProceduralSkyMaterial; ambient #1a1a22, low
energy; tonemap ACES, exposure ~1.0; fog #0e0f13 (volumetric on, desktop);
SSAO on; subtle glow. Cold low-energy DirectionalLight #9aa7c0. Torch
OmniLights #e08a3c with script-driven flicker (noise on energy). Rain
GPUParticles3D outdoors; ember particles near braziers. This alone turns
"polygon space" into a grim battlefield.

### 6.3 Materials — nothing visible keeps a default material
Every visible surface gets a material with an albedo texture: store texture
packs when installed, else procedural NoiseTexture2D/GradientTexture2D via
MaterialFactory (6.12). Roughness 0.6-0.95 varied. One static
MaterialFactory with caching; reuse materials.

### 6.4 Real-world scale (use everywhere)
| thing          | size                  |
|----------------|-----------------------|
| character      | 1.8 m tall            |
| wall height    | 4 m (gothic)          |
| doorway        | 2.2 H x 1.1 W m       |
| arena side     | 30-60 m               |
| greatsword     | 1.4 m                 |
| dagger         | 0.35 m                |
| brazier        | 1.1 m tall            |
| tombstone      | 0.7-1.0 m tall        |
Everything rests on the ground with collision. Nothing floats.

### 6.5 Build order
MAP -> ARENA/ZONE -> STRUCTURES -> PROPS -> CHARACTER -> DETAILS.
An arena with only a floor is INCOMPLETE.

### 6.6 Arena/zone kits (one screenshot must say "dark fantasy")
- Duel arena: broken ring wall, gothic arches, torch braziers at spawn,
  central obelisk, rubble, banners on poles.
- Skirmish map: two fortified spawns, ruined chapel or bridge mid, choke
  points, spike/pit hazards.
- Open world: ruined village chunks, shrine (objective), campfire camps,
  roaming elite mobs (light PvE for XP), contested shrines granting buffs.
- Isometric readability: distinct ground materials per zone, clear paths,
  silhouettes readable against the floor.
- Props from store kits when possible; Blender for hero pieces; primitives
  + textures as fallback. Retint everything to the palette (6.11).

### 6.7 Cheap details that sell the world (add everywhere)
Hanging chains (small cylinder segments); cobwebs (transparent quads);
rubble piles (MultiMesh stones); mud/darkened patches (dark quads slightly
above floor); waving banners (vertex-shader sway); fireflies/dust near
torches; fog drift.

### 6.8 Characters (required, not optional)
- Rigged humanoid from the store, retinted: armor tint per class + a player
  accent color. Isometric = SILHOUETTES CARRY READABILITY: each class needs
  a distinct outline (Warden broad + shield, Nightblade slim + two blades,
  Revenant hooded + staff). Faces still required on character models
  (character screen, zoom) — faceless mannequins are a FAILURE.
- Animations required: idle, run, attack chain, dodge roll, block, death,
  hit reaction. Store animation set, or Blender-rigged, or procedural
  sin() fallback — but SOMETHING must move.
- Nameplate + class icon + HP bar above heads; enemy nameplates red.
- Weapon meshes attached to hand bones/sockets; trails on swings.

### 6.9 Combat VFX (readability is gameplay)
All telegraphs drawn FLAT ON THE GROUND (decal rings/cones/lines), red for
enemy casts, aligned to actual hitboxes. Aim reticle under the cursor.
Hit sparks (GPUParticles3D) + damage numbers. Parry: white flash + 0.1 s
hit stop. Dodge: short motion blur/trail. Blood mist: small and tasteful.
Death: ragdoll or fall + fade.

### 6.10 UI bar
One code-generated Theme for ALL screens: bg #121014, accent #b08d57,
danger #7a1414, styled buttons/panels. Flow: splash -> login/register ->
character select/create (class cards with renders) -> server browser
(filters, ping, lock icon, direct IP join) -> lobby/queue -> match ->
results (MMR delta with tier progress, XP bar, loot gained). HUD: HP,
stamina, ability icons with cooldown sweeps, target frame, killfeed,
minimap (open world), respawn timer on death. Isometric HUD convention:
bottom-center ability bar, HP/stamina above it.

### 6.10 Palette (do not invent random colors)
| name      | hex     | use                    |
|-----------|---------|------------------------|
| stone     | #5a5a5e | ruins, walls           |
| moss      | #4a5a3a | overgrowth             |
| dark wood | #4a3b2d | gates, furniture       |
| bone      | #d8cfc0 | skulls, decor, text    |
| ember     | #e08a3c | fire, torch light      |
| blood     | #7a1414 | danger, enemy UI       |
| cold steel| #8a919c | weapons, armor trim    |
| arcane    | #6a4a8a | Revenant magic         |
| fog       | #0e0f13 | fog / ambient          |
| UI bg     | #121014 | UI background          |
| UI accent | #b08d57 | UI accent / gold       |

### 6.12 C# patterns (copy these habits into generators)
```csharp
// game/scripts/Player/IsoCameraRig.cs — the isometric camera rig
using Godot;

public partial class IsoCameraRig : Node3D
{
    [Export] public NodePath TargetPath;
    [Export] public float ZoomStep = 1f, MinZoom = 8f, MaxZoom = 18f;

    Node3D _target;
    Camera3D _cam;

    public override void _Ready()
    {
        _target = GetNode<Node3D>(TargetPath);
        _cam = GetNode<Camera3D>("Camera3D");
        RotationDegrees = new Vector3(0f, 45f, 0f);        // yaw locked
        _cam.Projection = Camera3D.ProjectionType.Orthogonal;
        _cam.Size = 12f;
        _cam.Far = 60f;
        _cam.Position = new Vector3(0f, 18f, 18f);         // above/behind
        _cam.RotationDegrees = new Vector3(-50f, 0f, 0f);  // pitch down
        Current = true;
    }

    public override void _Process(double delta)
    {
        GlobalPosition = GlobalPosition.Lerp(_target.GlobalPosition,
            1f - Mathf.Exp(-10f * (float)delta));          // smooth follow
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
                _cam.Size = Mathf.Max(MinZoom, _cam.Size - ZoomStep);
            if (mb.ButtonIndex == MouseButton.WheelDown)
                _cam.Size = Mathf.Min(MaxZoom, _cam.Size + ZoomStep);
        }
    }
}
```
```csharp
// game/scripts/Core/Aim.cs — cursor -> ground point (universal targeting)
using Godot;

public static class Aim
{
    public static Vector3 CursorGroundPoint(Camera3D cam)
    {
        var mouse = cam.GetViewport().GetMousePosition();
        var from = cam.ProjectRayOrigin(mouse);
        var dir = cam.ProjectRayNormal(mouse);
        return new Plane(Vector3.Up, 0f).IntersectsRay(from, dir)
               ?? cam.GlobalPosition;
    }
}
```
Material rule (MaterialFactory): every visible surface gets a cached
StandardMaterial3D with an albedo texture (store texture or
NoiseTexture2D/GradientTexture2D); AlbedoColor multiplies AlbedoTexture;
roughness 0.6-0.95; never a default material on anything visible.
Deterministic generation: `var rng = new RandomNumberGenerator { Seed = seed };`
Elo lives in Rating.cs implemented FULLY (numbers in BALANCE.md; no stubs).
C# gotchas: no `async void` in engine callbacks; never rename a .cs file
without renaming the class; dispose long-lived manual Resources.

### 6.13 Performance (desktop)
60+ FPS on mid hardware: <= 8 shadow-casting dynamic lights near the camera
(distant braziers shadowless); MultiMesh for tombstones/rubble/stones;
texture <= 1024 px; reused materials; arena generation deterministic from a
seed (server sends seed, clients build locally); bake NavigationRegion3D in
code after generation; fixed iso camera = stable frustum, set tight far
planes and rely on VisibilityNotifier culling.

## 7. COMBAT & CLASSES (condensed)
- Shared rules: stamina 100 (sprint/dodge/block drain), dodge roll 0.3 s
  i-frames, block -70% damage, parry window 0.25 s -> riposte, enemy
  telegraphs 0.5-0.8 s (ground decals), knockback small. Server computes all
  damage. Aiming: every skill targets the cursor ground point (Aim.cs);
  hitboxes are ground-projected shapes (circle/line/cone) validated
  server-side.
- Warden: 3-hit sword arc chain; shield bash (0.5 s stun, cone); warcry
  (ally buff radius); shield wall (100% block, 2 s).
- Nightblade: fast dual-dagger chain; shadow step (6 m blink toward cursor,
  8 s cd); stealth 5 s (breaks on attack, next hit +50%); smoke bomb
  (enemy blind zone).
- Revenant: bone spear (ground-line projectile); life drain (channel,
  line); grave grasp (1 s root, circle); soul ward (absorb shield).
- Design NEW content data-driven: classes/abilities/affixes live in data
  resources + BALANCE.md, so invention means editing data, not code.
- BALANCE HARNESS: headless bot-vs-bot matches per class matchup, prints a
  winrate matrix to the console. Target 45-55% per matchup. Every combat
  change must end with a harness run; tune numbers in BALANCE.md.

## 8. PROGRESSION, RANKING, LOOT
- XP from kills, objectives, match results. Level curve: XP(N) = 100 * N^1.5
  (tune in BALANCE.md). Levels unlock abilities/passives/cosmetics; raw stat
  gain capped at +10% at max level (horizontal progression — MANDATORY for
  fair PvP).
- MMR: Elo (BALANCE.md numbers), start 1000, K=32, one MMR per mode. Tiers:
  Ash -> Iron -> Bronze -> Silver -> Gold -> Obsidian -> Crown. Leaderboard
  per mode from central; visible in client and results screen.
- Matchmaking: Quick Play -> central returns the least-full live server for
  the mode (skill-aware later). Private matches: password servers + direct
  IP join. Party system is a later task.
- Loot: procedural items — rarity common -> mythic, generated name
  (prefix + base + suffix from BALANCE.md tables), affixes; equipment changes
  the character's tint/attached meshes. Drops reported by the match server,
  stored centrally.

## 9. PROJECT LAYOUT
game/{Scripts/{Core,Player,Combat,Classes,World,Networking,UI,Audio,Save},
Scenes, Assets/{Models,Textures,Audio}, Shaders} | central/ | shared/ |
tests/ | tools/ | ATTRIBUTION.md | BALANCE.md
tools/ must contain: setup(.sh/.bat), build_windows(.sh/.bat),
build_linux(.sh/.bat), build_all(.sh/.bat), test(.sh/.bat), remote_test.sh.
Scripts report real errors and use exit codes. Never fake a successful build.

## 10. DONE = ALL GATES PASS
1. `dotnet build Hollowcrown.sln`: zero errors.
2. Runs with zero script errors in the remote test environment.
3. Screenshot evidence exists (menu / character screen / isometric arena /
   combat action with telegraphs + HUD).
4. Visual standard (Section 6) respected in the changed area.
5. Combat verified: move, cursor-aim, dodge, block/parry, attack, kill,
   respawn; camera never clips through walls (occlusion fade works).
6. Multiplayer smoke test: dedicated server boots headless AND a client (or
   bots) connects and fights.
7. If progression touched: central round-trip verified (login -> character
   -> save -> reload shows the same data).
8. ATTRIBUTION.md updated if assets added; BALANCE.md + harness run if
   combat tuned.
9. Committed and pushed. No TODOs left.
A failed gate: fix it in the same iteration, or revert.

## 11. AUTONOMY
You are the lead developer. Be inventive within this spec: invent class
kits, affixes, zone layouts, lore names. Ask the user only for missing
files or access — never invent file contents.
