# HOLLOWCROWN — SPEC v1 (dark fantasy PvP MMO; this file overrides all older docs)

## 0. READ FIRST (every session)
1. LOOKS ARE THE GAME. Flat gray boxes, empty arenas, default materials =
   FAILED task. Grim dark-fantasy atmosphere is a core requirement, not polish.
2. LANGUAGE: C# (.NET) on Godot 4.x. GDScript is FORBIDDEN. Every script is a
   `public partial class` in a file whose name equals the class name. The
   central server is also C#.
3. ASSETS: search/install via the Godot store MCP before hand-building
   anything. Only CC0 / CC-BY / MIT licenses. Log every install in
   ATTRIBUTION.md in the same commit. Blender MCP: use it whenever available.
4. USE ALL MCP TOOLS: playtester (remote testing), Godot store (assets),
   Blender (custom meshes), web search (verify APIs), GitHub search (license-
   safe reference patterns), Playwright (docs scraping). Timeboxed: one pass
   per need; browsing never consumes a whole iteration.
5. Every iteration: implement -> dotnet build -> commit/push -> test per
   aider_continue.md (remote playtester + screenshots) -> judge -> fix.
6. Never restart or redesign the project. Evolve existing code.
7. Windows + Linux desktop ONLY. No mobile targets anywhere.
8. PvP IS THE GAME. Every system must serve skill-based PvP.

## 1. GAME
HOLLOWCROWN — dark fantasy PvP MMO. Godot 4.x + C#/.NET, third-person camera.
- Grim atmospheric world: gothic ruins, perpetual dusk, fog, embers, rain.
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
  timing, stamina, telegraphed abilities, hitboxes. NO tab-target.

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
1. Core combat feel: movement, dodge, block, parry, hit feedback.
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

### 6.1 Global atmosphere (do this once, early)
WorldEnvironment: overcast ProceduralSkyMaterial; ambient #1a1a22, low
energy; tonemap ACES, exposure ~1.0; fog #0e0f13 (volumetric on, desktop);
SSAO on; subtle glow. Cold low-energy DirectionalLight #9aa7c0. Torch
OmniLights #e08a3c with script-driven flicker (noise on energy). Rain
GPUParticles3D outdoors; ember particles near braziers. This alone turns
"polygon space" into a grim battlefield.

### 6.2 Materials — nothing visible keeps a default material
Every visible surface gets a material with an albedo texture: store texture
packs when installed, else procedural NoiseTexture2D/GradientTexture2D via
MaterialFactory (6.11). Roughness 0.6-0.95 varied. One static
MaterialFactory with caching; reuse materials.

### 6.3 Real-world scale (use everywhere)
| thing          | size                  |
|----------------|-----------------------|
| eye height     | 1.65 m                |
| wall height    | 4 m (gothic)          |
| doorway        | 2.2 H x 1.1 W m       |
| arena side     | 30-60 m               |
| greatsword     | 1.4 m                 |
| dagger         | 0.35 m                |
| brazier        | 1.1 m tall            |
| tombstone      | 0.7-1.0 m tall        |
Everything rests on the ground with collision. Nothing floats.

### 6.4 Build order
MAP -> ARENA/ZONE -> STRUCTURES -> PROPS -> CHARACTER -> DETAILS.
An arena with only a floor is INCOMPLETE.

### 6.5 Arena/zone kits (one screenshot must say "dark fantasy")
- Duel arena: broken ring wall, gothic arches, torch braziers at spawn,
  central obelisk, rubble, banners on poles.
- Skirmish map: two fortified spawns, ruined chapel or bridge mid, choke
  points, spike/pit hazards.
- Open world: ruined village chunks, shrine (objective), campfire camps,
  roaming elite mobs (light PvE for XP), contested shrines granting buffs.
- Props from store kits when possible; Blender for hero pieces; primitives
  + textures as fallback. Retint everything to the palette (6.10).

### 6.6 Cheap details that sell the world (add everywhere)
Hanging chains (small cylinder segments); cobwebs (transparent quads);
rubble piles (MultiMesh stones); mud/darkened patches (dark quads slightly
above floor); waving banners (vertex-shader sway); fireflies/dust near
torches; fog drift.

### 6.7 Characters (required, not optional)
- Rigged humanoid from the store, retinted: armor tint per class + a player
  accent color. THIRD-PERSON means faces and armor are ALWAYS visible —
  faceless mannequins are a FAILURE.
- Animations required: idle, run, attack chain, dodge roll, block, death,
  hit reaction. Store animation set, or Blender-rigged, or procedural
  sin() fallback — but SOMETHING must move.
- Nameplate + class icon + HP bar above heads; enemy nameplates red.
- Weapon meshes attached to hand bones/sockets; trails on swings.

### 6.8 Combat VFX (readability is gameplay)
Telegraphs: decal rings/cones on the ground, red for enemy casts. Hit
sparks (GPUParticles3D) + damage numbers. Parry: white flash + 0.1 s hit
stop. Dodge: short motion blur/trail. Blood mist: small and tasteful.
Death: ragdoll or fall + fade.

### 6.9 UI bar
One code-generated Theme for ALL screens: bg #121014, accent #b08d57,
danger #7a1414, styled buttons/panels. Flow: splash -> login/register ->
character select/create (class cards with renders) -> server browser
(filters, ping, lock icon, direct IP join) -> lobby/queue -> match ->
results (MMR delta with tier progress, XP bar, loot gained). HUD: HP,
stamina, ability icons with cooldown sweeps, target frame, killfeed,
minimap (open world), respawn timer on death.

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

### 6.11 C# patterns (copy these habits into generators)
```csharp
// game/scripts/Core/MaterialFactory.cs — file name must match class name
using Godot;
using System.Collections.Generic;

public static class MaterialFactory
{
    static readonly Dictionary<string, StandardMaterial3D> Cache = new();

    public static StandardMaterial3D Flat(Color c, float rough = 0.9f)
    {
        string key = $"flat:{c}:{rough}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var m = new StandardMaterial3D { AlbedoColor = c, Roughness = rough };
        Cache[key] = m;
        return m;
    }

    public static StandardMaterial3D Grime(Color baseColor, float rough = 0.9f)
    {
        string key = $"grime:{baseColor}:{rough}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var m = new StandardMaterial3D
        {
            AlbedoColor = baseColor, // AlbedoColor multiplies AlbedoTexture
            AlbedoTexture = new NoiseTexture2D
            {
                Noise = new FastNoiseLite { Frequency = 0.05f },
                Width = 256, Height = 256,
            },
            Roughness = rough,
        };
        Cache[key] = m;
        return m;
    }
}
```
```csharp
// game/scripts/Core/Rating.cs — Elo + tiers (numbers also mirrored in BALANCE.md)
public static class Rating
{
    public static int Expected(int a, int b) { /* Ea = 1 / (1 + 10^((Rb-Ra)/400)) */ throw new System.NotImplementedException(); }
    public static int Update(int ra, int rb, bool aWon, double k = 32.0) { /* ra + k * (S - Ea) */ throw new System.NotImplementedException(); }
    // TODO-FORBIDDEN: implement fully before committing — never ship stubs.
}
```
```csharp
// game/scripts/World/TorchFlicker.cs — attach to a light
using Godot;

public partial class TorchFlicker : OmniLight3D
{
    FastNoiseLite _noise = new() { Frequency = 3f };
    float _t = 0f;

    public override void _Process(double delta)
    {
        _t += (float)delta * 4f;
        LightEnergy = 2.2f + 0.7f * _noise.GetNoise1D(_t); // #e08a3c base
    }
}
```
Deterministic generation: `var rng = new RandomNumberGenerator { Seed = seed };`
C# gotchas: no `async void` in engine callbacks; never rename a .cs file
without renaming the class; dispose long-lived manual Resources.

### 6.12 Performance (desktop)
60+ FPS on mid hardware: <= 8 shadow-casting dynamic lights near the camera
(distant braziers shadowless); MultiMesh for tombstones/rubble/stones;
texture <= 1024 px; reused materials; arena generation deterministic from a
seed (server sends seed, clients build locally); bake NavigationRegion3D in
code after generation.

## 7. COMBAT & CLASSES (condensed)
- Shared rules: stamina 100 (sprint/dodge/block drain), dodge roll 0.3 s
  i-frames, block -70% damage, parry window 0.25 s -> riposte, enemy
  telegraphs 0.5-0.8 s, knockback small. Server computes all damage.
- Warden: 3-hit sword chain; shield bash (0.5 s stun); warcry (ally buff);
  shield wall (100% block, 2 s).
- Nightblade: fast dual-dagger chain; shadow step (6 m blink, 8 s cd);
  stealth 5 s (breaks on attack, next hit +50%); smoke bomb (enemy blind).
- Revenant: bone spear (projectile); life drain (channel); grave grasp
  (1 s root); soul ward (absorb shield). Arcane palette.
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
- MMR: Elo (6.11), start 1000, K=32, one MMR per mode. Tiers: Ash -> Iron ->
  Bronze -> Silver -> Gold -> Obsidian -> Crown. Leaderboard per mode from
  central; visible in client and results screen.
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
3. Screenshot evidence exists (menu / character screen / arena / combat).
4. Visual standard (Section 6) respected in the changed area.
5. Combat verified: move, dodge, block/parry, attack, kill, respawn.
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
