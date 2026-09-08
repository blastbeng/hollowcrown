using System.Collections.Generic;
using Godot;
using Hollowcrown.Combat;
using Hollowcrown.Player;
using Hollowcrown.Save;
using Hollowcrown.Shared;
using Hollowcrown.UI;
using Hollowcrown.World;

namespace Hollowcrown.Networking;

/// <summary>
/// Server-authoritative combat brain + realm session manager (Vision 2.3/4):
/// the MATCH SERVER owns every HP number and the peer roster. Clients only
/// REQUEST hits and MIRROR broadcasts; the server validates each hit against
/// ITS OWN world — range, arc, per-peer cooldown, sane buff caps — before
/// applying anything. Offline (no real peer) the local peer IS the authority,
/// so single-player behaves identically to a networked realm.
///
/// Realm handshake (Vision 4): clients authenticate with the realm password
/// right after ENet connect; a wrong password is a server-side kick. Approved
/// peers get a deterministic spawn point and become combat targets
/// (CombatId == ENet peer id). Positions sync client->server at 10 Hz and are
/// relayed to everyone else so RemoteAvatar puppets can mirror the fight.
///
/// RPC layout: every peer runs the same scene tree, so this node lives at
/// /root/Main/CombatAuthority on client AND dedicated server, added with
/// force_readable_name (Godot high-level multiplayer rules, docs-verified).
/// Target ids: players use their ENet peer id; static world targets (training
/// dummy) start at 1000, assigned in identical build order on every peer.
/// </summary>
public partial class CombatAuthority : Node
{
    public const float RespawnDelay = 3.0f;          // BALANCE.md: dummy_respawn
    public const int PlayerMaxHp = 100;              // BALANCE.md: player_hp
    private const float RangeSlack = 0.35f;          // victim half-width tolerance
    private const float MaxBuffMultiplier = 1.25f;   // sane cap (anti-cheat)
    private const double BuffDuration = 10.0;        // BALANCE.md: warden_warcry
    private const double PositionInterval = 0.1;     // 10 Hz position sync
    private const double BeatInterval = 5.0;         // server log beat

    /// <summary>Duel spawn points (Vision 6.6): two opposed sides, clear of
    /// obelisk/dummy/braziers. Deterministic on every peer.</summary>
    public static readonly Vector3[] SpawnPoints =
        { new(-5f, 0.2f, 8f), new(5f, 0.2f, -8f) };

    [Signal] public delegate void KillFeedEventHandler(string text);

    /// <summary>Loot slice 2 (Vision 8): fired when the AUTHORITY grants a
    /// pickup to a peer — the inventory panel refreshes on it.</summary>
    [Signal] public delegate void LootGrantedEventHandler(string itemName, string rarityLabel);

    /// <summary>Loot slice 2 (Vision 8): fired when a peer (re-)equips an
    /// item — the model retints on it (equipment changes the character's
    /// look).</summary>
    [Signal] public delegate void EquipmentChangedEventHandler(int peerId);

    /// <summary>Password the NEXT outbound connection presents at handshake
    /// (set by ServerBrowser.Join or the --join launch flag before dialing).</summary>
    public static string PendingPassword = "";

    /// <summary>Class id the NEXT outbound connection declares at handshake
    /// ("warden"/"nightblade"/"revenant") — the server needs it for display
    /// names and so every peer spawns the right enemy model variant.</summary>
    public static string PendingClass = "warden";

    /// <summary>Central character id of the NEXT outbound connection (MMR
    /// Vision 8: the match server reports Elo per CHARACTER, so the handshake
    /// must carry who is playing). 0 = no central character (offline/test).</summary>
    public static long PendingCharacterId;

    /// <summary>Remote-peer gear gap (Vision 8 NEXT 1): derived gear stats the
    /// NEXT outbound connection DECLARES at handshake. Session gear lives only
    /// on the owning client, so the server otherwise capped remote peers at
    /// the base 100 HP with no haste and no gear ward. Filled by SendHandshake
    /// from ProgressionSession; 0 = no session (offline/test). Only STATS flow
    /// — tint/weapon visuals stay local-session by design.</summary>
    public static int PendingGearVitality;
    public static int PendingGearPower;
    public static int PendingGearHaste;
    public static int PendingGearWard;

    /// <summary>Match-server token (Vision 4): set by DedicatedServer at
    /// registration, relayed by the CLIENT on progression saves (the server
    /// token identifies the realm; the user token identifies the player).</summary>
    public static string ServerToken = "";

    /// <summary>Central base URL the match server reports MMR against (set by
    /// DedicatedServer from --central / HC_CENTRAL_URL; default when unset).</summary>
    public static string CentralBaseUrl = CentralClient.EnvBaseUrl();

    /// <summary>Vision 8: fired when the AUTHORITY resolves a duel Elo result
    /// (central applied) — the results screen + HUD read the session mirror;
    /// the signal exists for killfeed-style surfacing later.</summary>
    [Signal] public delegate void MmrResolvedEventHandler(long winnerCharacterId, long loserCharacterId,
        int winnerMmr, int loserMmr, int winnerDelta, int loserDelta,
        string winnerTier, string loserTier);

    private sealed class PeerInfo
    {
        public bool Approved;
        public int SpawnIndex;
        public string ClassId = "warden";
        public long CharacterId;          // central character (MMR attribution)
        public Vector3 Position;
        public float Yaw;
        public ICombatTarget? Target;
        // Remote-peer gear gap (Vision 8 NEXT 1): derived stats declared at
        // handshake — max HP (vitality), haste floor and the gear-ward pool
        // now apply to remote peers too. Power is stored for the server-side
        // damage mirror (damage validation still reads CombatTables only).
        public int Vitality;
        public int Power;
        public int Haste;
        public int Ward;
    }

    /// <summary>Server-side data-only record for a remote player: no visuals,
    /// but it satisfies hit validation (CombatPosition = last report).</summary>
    private sealed class PeerTargetRecord : ICombatTarget
    {
        private readonly CombatAuthority _auth;
        public PeerTargetRecord(CombatAuthority auth, int id, string name)
        {
            _auth = auth;
            CombatId = id;
            DisplayName = name;
        }
        public int CombatId { get; }
        public string DisplayName { get; }
        // Handshake-declared vitality for real peers; base fallback otherwise.
        public int MaxHp => _auth.MaxHpOfPeer(CombatId);
        public int Hp => _auth._hp.TryGetValue(CombatId, out int hp) ? hp : MaxHp;
        public bool IsDead => Hp <= 0;
        public Vector3 CombatPosition => _auth._peers.TryGetValue(CombatId, out var info)
            ? info.Position
            : Vector3.Zero;
        public void AssignCombatId(int id) { }
        public void OnHitApplied(int amount, bool heavy, int hpAfter) { }
        public void OnStunned(float seconds) { }
        public void OnStealthed(bool stealthed) { }
        public void OnRooted(float seconds) { }
        public void OnWard(float amount) { }
        public void OnHealed(int hpAfter) { }
        public void OnKilled() { }
        public void OnRespawned(int hpAfter, Vector3 spawnPos) { }
        public void OnProgress(int kills, int xp) { }

        public void OnMmr(long winnerCharacterId, long loserCharacterId, int winnerMmr,
            int loserMmr, int winnerDelta, int loserDelta, string winnerTier, string loserTier)
        { }
    }

    private readonly Dictionary<int, PeerInfo> _peers = new();
    private readonly Dictionary<int, ICombatTarget> _targets = new();
    private readonly Dictionary<int, int> _hp = new();
    private readonly Dictionary<int, int> _maxHp = new();
    private readonly Dictionary<int, Vector3> _respawnPos = new();
    private readonly Dictionary<(int Peer, int Attack), double> _lastHitAt = new();
    private readonly Dictionary<int, (float Mult, double Until)> _buffs = new();
    private readonly Dictionary<int, double> _respawnAt = new();
    // Nightblade (Vision 7): stealth state + per-peer smoke/stealth cooldowns.
    private readonly Dictionary<int, double> _stealthUntil = new();
    private readonly Dictionary<int, double> _lastSmokeAt = new();
    private readonly Dictionary<int, double> _lastStealthAt = new();
    private readonly Dictionary<int, float> _wards = new();          // absorb pools
    private readonly Dictionary<int, double> _wardUntil = new();
    private readonly Dictionary<int, double> _lastWardAt = new();
    // Progression (Vision 8): kills + XP per combat id — server-owned, the
    // local player's mirror feeds the HUD and the results screen.
    private readonly Dictionary<int, int> _kills = new();
    private readonly Dictionary<int, int> _xp = new();
    // Ranking (Vision 8): ONE Elo resolution per duel pair per realm session
    // (a duel mode match ends at the first kill — respawn rematches inside
    // the same realm are free warmup, not new rated results). A fresh realm
    // (new authority node) reports again.
    private readonly HashSet<(int Winner, int Loser)> _mmrReported = new();
    private int _nextDropId = 1;
    private int _nextSmokeZone;
    private double _lootScanAccum;
    private int _nextId = 1000;                      // static world targets
    private int _spawnCounter;
    private double _positionAccum, _beatAccum;

    /// <summary>True only with a REAL peer — the default OfflineMultiplayerPeer
    /// (always set, always id 1, server mode) counts as offline single-player.
    /// Offline, broadcast senders invoke the RPC bodies directly.</summary>
    public bool Networked => Multiplayer.HasMultiplayerPeer() &&
        Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;

    public bool IsAuthorityMode => !Networked || Multiplayer.IsServer();

    private int MyPeerId => Networked ? Multiplayer.GetUniqueId() : 1;

    // ---------- per-peer gear stats (remote-peer gear gap, NEXT 1) ----------

    /// <summary>Server truth: base 100 + handshake-declared vitality for real
    /// peers; the local session's derived value for peer 1 / harness bots;
    /// fallback base when the roster does not know the peer.</summary>
    private int MaxHpOfPeer(int peerId) =>
        _maxHp.TryGetValue(peerId, out int known)
            ? known
            : _peers.TryGetValue(peerId, out var info) && info.Vitality > 0
                ? PlayerMaxHp + info.Vitality
                : peerId == 1 || (peerId >= 500 && peerId < 1000)
                    ? ProgressionSession.DerivedMaxHp
                    : PlayerMaxHp;

    /// <summary>Server truth: handshake-declared haste affixes for real peers,
    /// the local session for peer 1 / harness bots, else none.</summary>
    private int GearHasteOfPeer(int peerId) =>
        _peers.TryGetValue(peerId, out var info) && info.Approved
            ? info.Haste
            : peerId == 1 || (peerId >= 500 && peerId < 1000)
                ? ProgressionSession.DerivedHaste
                : 0;

    /// <summary>Server truth: handshake-declared gear ward for remote peers,
    /// the local session's derived pool for peer 1 / harness bots.</summary>
    private int GearWardOfPeer(int peerId) =>
        _peers.TryGetValue(peerId, out var info) && info.Approved
            ? info.Ward
            : peerId == 1 || (peerId >= 500 && peerId < 1000)
                ? ProgressionSession.DerivedWard
                : 0;

    public static CombatAuthority? For(Node node) =>
        node.GetTree().GetFirstNodeInGroup("combat_authority") as CombatAuthority;

    public override void _Ready()
    {
        AddToGroup("combat_authority");
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += SendHandshake;
        Multiplayer.ServerDisconnected += OnServerGone;
        // Belt and braces: if we attached after the ENet connect already
        // completed (JoinRealm path), the ConnectedToServer event is gone —
        // present the handshake now (idempotent: the server ignores replays).
        if (Networked && !IsAuthorityMode)
            SendHandshake();
        GD.Print($"COMBAT AUTHORITY READY — networked={Networked} authority={IsAuthorityMode}");
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= SendHandshake;
        Multiplayer.ServerDisconnected -= OnServerGone;
    }

    // --------------------------- realm session -----------------------------

    private void OnPeerConnected(long id)
    {
        if (!IsAuthorityMode)
            return;   // clients learn about peers via SpawnPlayer broadcasts
        int peerId = (int)id;
        _peers[peerId] = new PeerInfo
        {
            Approved = false,
            SpawnIndex = _spawnCounter++ % SpawnPoints.Length,
        };
        GD.Print($"REALM: peer {peerId} connected — awaiting handshake");
    }

    private void OnPeerDisconnected(long id)
    {
        if (!IsAuthorityMode)
            return;
        int peerId = (int)id;
        if (!_peers.Remove(peerId))
            return;
        if (_targets.ContainsKey(peerId))
        {
            _targets.Remove(peerId);
            _hp.Remove(peerId);
            _maxHp.Remove(peerId);
            _respawnPos.Remove(peerId);
            _respawnAt.Remove(peerId);
            SendDespawnPlayer(peerId);
        }
        GD.Print($"REALM: peer {peerId} left the realm");
    }

    private void OnServerGone()
    {
        // Any disconnect (kick, crash, network): resume offline ownership.
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        GD.Print("REALM: server gone — combat authority back to local mode");
    }

    private void SendHandshake()
    {
        // Remote-peer gear gap (Vision 8 NEXT 1): the client DECLARES its
        // derived gear stats with the handshake — session gear lives only on
        // the owning client, so this is the server's only chance to learn
        // vitality/haste/ward (and power for the damage mirror). Safe on
        // every join path (--join, harness, browser): fallback 0 when no
        // central character was selected.
        if (!IsAuthorityMode)
        {
            PendingGearVitality = ProgressionSession.CharacterId > 0
                ? ProgressionSession.StatTotal("vitality") : 0;
            PendingGearPower = ProgressionSession.CharacterId > 0
                ? ProgressionSession.StatTotal("power") : 0;
            PendingGearHaste = ProgressionSession.CharacterId > 0
                ? ProgressionSession.StatTotal("haste") : 0;
            PendingGearWard = ProgressionSession.CharacterId > 0
                ? ProgressionSession.StatTotal("ward") : 0;
        }
        RpcId(1, nameof(HandshakeRpc), PendingPassword, PendingClass, PendingCharacterId,
            PendingGearVitality, PendingGearPower, PendingGearHaste, PendingGearWard);
        GD.Print($"REALM: handshake sent (character={PendingCharacterId} " +
                 $"gear vit={PendingGearVitality} power={PendingGearPower} " +
                 $"haste={PendingGearHaste} ward={PendingGearWard}) — awaiting approval");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HandshakeRpc(string password, string classId, long characterId,
        int vitality, int power, int haste, int ward)
    {
        int peer = Multiplayer.GetRemoteSenderId();
        if (!_peers.TryGetValue(peer, out var info))
        {
            // The client's handshake can beat the server's connect event.
            info = new PeerInfo
            {
                SpawnIndex = _spawnCounter++ % SpawnPoints.Length,
                ClassId = classId,
            };
            _peers[peer] = info;
        }
        if (info.Approved)
            return;

        // Always apply the DECLARED class (the connect event may have won the
        // race and created the record with the default id).
        info.ClassId = classId;
        info.CharacterId = characterId;
        // Remote-peer gear gap (Vision 8 NEXT 1): stored per peer so
        // MaxHpOfPeer/GearHasteOfPeer/GearWardOfPeer serve the server truth
        // for every combatant, not just the local session.
        info.Vitality = Mathf.Max(0, vitality);
        info.Power = Mathf.Max(0, power);
        info.Haste = Mathf.Max(0, haste);
        info.Ward = Mathf.Max(0, ward);

        if (password != DedicatedServer.RealmPassword)
        {
            GD.Print($"REALM: peer {peer} KICKED — wrong password");
            _peers.Remove(peer);   // gone before it can spawn or fight
            (Multiplayer as SceneMultiplayer)?.DisconnectPeer(peer);
            return;
        }

        info.Approved = true;
        Vector3 spawn = SpawnPoints[info.SpawnIndex];
        string name = $"{PlayerClassInfo.Label(PlayerClassInfo.FromId(classId))}#{peer}";
        var record = new PeerTargetRecord(this, peer, name);
        info.Target = record;
        _targets[peer] = record;
        // Loot slice 2 (Vision 8): vitality affixes on EQUIPPED gear raise
        // max HP — server truth from the handshake-declared stats now (the
        // client mirror takes the SAME number in SpawnPlayerRpc).
        int maxHp = MaxHpOfPeer(peer);
        _hp[peer] = maxHp;
        _maxHp[peer] = maxHp;
        _respawnPos[peer] = spawn;
        info.Position = spawn;
        SendSpawnPlayer(peer, spawn, name, info.ClassId, maxHp);
        // Vision 4: hand the approved peer the realm's match-server token —
        // the client relays it (X-Server-Token) on progression saves. Never
        // sent to unapproved peers.
        RpcId(peer, nameof(ServerTokenRpc), ServerToken);
        GD.Print($"REALM: peer {peer} approved ({info.ClassId}) — spawns at {spawn}");

        // Catch the new peer up with everyone already approved in the realm
        // (late joiners miss earlier broadcast spawns).
        foreach (var (otherId, other) in _peers)
        {
            if (otherId == peer || !other.Approved)
                continue;
            int otherMaxHp = MaxHpOfPeer(otherId);
            RpcId(peer, nameof(SpawnPlayerRpc), otherId,
                SpawnPoints[other.SpawnIndex],
                $"{PlayerClassInfo.Label(PlayerClassInfo.FromId(other.ClassId))}#{otherId}",
                other.ClassId, otherMaxHp);
        }
    }

    private void SendSpawnPlayer(int peerId, Vector3 spawnPos, string displayName,
        string classId, int maxHp)
    {
        if (Networked)
            Rpc(nameof(SpawnPlayerRpc), peerId, spawnPos, displayName, classId, maxHp);
        else
            SpawnPlayerRpc(peerId, spawnPos, displayName, classId, maxHp);
    }

    private void SendDespawnPlayer(int peerId)
    {
        if (Networked)
            Rpc(nameof(DespawnPlayerRpc), peerId);
        else
            DespawnPlayerRpc(peerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerTokenRpc(string token)
    {
        // Vision 4: the match server hands its token to approved peers so the
        // client can RELAY progression saves (user token = player, X-Server-Token
        // = realm). The client never invents it; a realm that never registered
        // sends an empty string and saves fail closed.
        ServerToken = token;
        GD.Print($"REALM: match-server token received ({(token.Length == 0 ? "none" : token[..8] + "…")})");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnPlayerRpc(int peerId, Vector3 spawnPos, string displayName,
        string classId, int maxHp)
    {
        var arena = GetParent().GetNodeOrNull<Node3D>("Arena");

        if (peerId == MyPeerId)
        {
            // My spawn approval: reposition the local warden + re-register
            // under the real ENet peer id (boot-time registration used id 1).
            // The balance harness joins REALMS with bot bodies, not the UI
            // player — the bots are Main children (Bot0/Bot1) and register
            // here too.
            var bot = GetParent().GetNodeOrNull<CombatBot>($"Bot{peerId - 1}");
            if (bot is not null)
            {
                bot.GlobalPosition = spawnPos;
                bot.Velocity = Vector3.Zero;
                bot.AssignCombatId(peerId);
                RegisterBot(bot);
            }
            var player = arena?.GetNodeOrNull<PlayerController>("Player");
            if (player is not null)
            {
                player.GlobalPosition = spawnPos;
                player.Velocity = Vector3.Zero;
                RegisterPlayer(player, peerId);
            }
            GD.Print($"REALM: spawned {displayName} (self) at {spawnPos}");
        }
        else if (!IsAuthorityMode)
        {
            // Clients: puppet for the other warden (Vision 6.8 silhouette).
            if (_targets.Remove(peerId, out var old) && old is Node oldNode)
                oldNode.QueueFree();
            var avatar = new RemoteAvatar
            {
                Name = $"Remote{peerId}",
                PeerId = peerId,
                DisplayName = displayName,
                ClassId = classId,
                Position = spawnPos,
            };
            arena?.AddChild(avatar, forceReadableName: true);
            // Server-declared max HP (handshake vitality) feeds the avatar's
            // nameplate bar — after AddChild, the bar nodes exist.
            avatar.SetMaxHp(maxHp);
            if (_peers.TryGetValue(peerId, out var info))
                info.Target = avatar;
            _targets[peerId] = avatar;
            // Client mirror takes the server's max HP (handshake vitality) —
            // gear-vitality victims used to mirror base 100 here.
            _hp[peerId] = maxHp;
            _maxHp[peerId] = maxHp;
            _respawnPos[peerId] = spawnPos;
            GD.Print($"REALM: {displayName} spawned (avatar) at {spawnPos}");
        }
        else
        {
            GD.Print($"REALM: server recorded spawn of peer {peerId} at {spawnPos}");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void DespawnPlayerRpc(int peerId)
    {
        if (_targets.Remove(peerId, out var target) && target is Node node)
            node.QueueFree();
        _hp.Remove(peerId);
        _maxHp.Remove(peerId);
        _respawnPos.Remove(peerId);
        _respawnAt.Remove(peerId);
        if (_peers.TryGetValue(peerId, out var info))
            info.Target = null;
        GD.Print($"REALM: peer {peerId} despawned");
    }

    // --------------------------- position sync -----------------------------

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReportPositionRpc(Vector3 pos, float yaw)
    {
        int peer = Multiplayer.GetRemoteSenderId();
        if (!_peers.TryGetValue(peer, out var info))
            return;
        info.Position = pos;
        info.Yaw = yaw;
        // Relay to every OTHER peer (mirrors need it; the sender does not).
        foreach (int other in _peers.Keys)
            if (other != peer)
                RpcId(other, nameof(PeerPositionRpc), peer, pos, yaw);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void PeerPositionRpc(int peerId, Vector3 pos, float yaw)
    {
        if (_targets.TryGetValue(peerId, out var target) && target is RemoteAvatar avatar)
            avatar.SetNetworkTransform(pos, yaw);
        else if (_peers.TryGetValue(peerId, out var info))
        {
            info.Position = pos;
            info.Yaw = yaw;
        }
    }

    // ------------------------------ registry -------------------------------

    /// <summary>Static world targets (dummy): id assigned from 1000 in the
    /// identical arena build order every peer runs.</summary>
    public void RegisterWorldTarget(ICombatTarget target)
    {
        int id = _nextId++;
        _targets[id] = target;
        _hp[id] = target.MaxHp;
        _maxHp[id] = target.MaxHp;
        _respawnPos[id] = target.CombatPosition;
        target.AssignCombatId(id);
        GD.Print($"AUTHORITY: registered \"{target.DisplayName}\" id={id} max_hp={target.MaxHp}");
    }

    /// <summary>The local warden. Boot-time it registers under the current
    /// peer id (1 offline); after handshake approval it re-registers under
    /// the real ENet id via SpawnPlayerRpc.</summary>
    public void RegisterSelf(PlayerController player)
    {
        RegisterPlayer(player, MyPeerId);
    }

    /// <summary>Balance-harness bots (Vision 7): same registration path as
    /// the local player, under the bot's combat id (offline 500+, networked
    /// its real ENet peer id once approved).</summary>
    public void RegisterBot(CombatBot bot)
    {
        // A joining bot re-registers from its boot id — drop the stale row.
        foreach (var (id, target) in _targets)
        {
            if (target == bot && id != bot.CombatId)
            {
                _targets.Remove(id);
                _hp.Remove(id);
                _maxHp.Remove(id);
                _respawnPos.Remove(id);
                break;
            }
        }
        _targets[bot.CombatId] = bot;
        _hp[bot.CombatId] = bot.MaxHp;
        _maxHp[bot.CombatId] = bot.MaxHp;
        _respawnPos[bot.CombatId] = bot.CombatPosition;
        GD.Print($"AUTHORITY: bot \"{bot.DisplayName}\" registered id={bot.CombatId} max_hp={bot.MaxHp}");
    }

    private void RegisterPlayer(PlayerController player, int peerId)
    {
        foreach (var (id, target) in _targets)
        {
            if (target == player && id != peerId)
            {
                _targets.Remove(id);
                _hp.Remove(id);
                _maxHp.Remove(id);
                _respawnPos.Remove(id);
                break;
            }
        }
        _targets[peerId] = player;
        // The local body always mirrors its OWN session (vitality affixes);
        // remote peers take the handshake-declared number instead (this
        // registration used to hardcode base 100).
        int maxHp = ProgressionSession.DerivedMaxHp;
        _hp[peerId] = maxHp;
        _maxHp[peerId] = maxHp;
        _respawnPos[peerId] = SpawnPoints[0];
        player.AssignCombatId(peerId);
        GD.Print($"AUTHORITY: local warden registered id={peerId} max_hp={maxHp}");
    }

    public void Unregister(int id)
    {
        _targets.Remove(id);
        _hp.Remove(id);
        _maxHp.Remove(id);
        _respawnPos.Remove(id);
        _respawnAt.Remove(id);
    }

    // --------------------------- client requests ---------------------------
    // Clients ask; the server decides. No local state is touched here.

    public void RequestHit(int victimId, int attackId, Vector3 attackerPos, Vector3 facing)
    {
        if (IsAuthorityMode)
            ValidateAndApply(MyPeerId, victimId, attackId, attackerPos, facing);
        else
            RpcId(1, nameof(SubmitHitRpc), victimId, attackId, attackerPos, facing);
    }

    /// <summary>Balance-harness entry (Vision 7): same validation path, but
    /// the attacking peer id is EXPLICIT — offline bots are not the local
    /// player (peer 1), so the harness can attribute hits to each bot.
    /// Networked bots go through RequestHit (socket sender is authoritative).</summary>
    public void RequestHitAs(int attackerId, int victimId, int attackId,
        Vector3 attackerPos, Vector3 facing)
    {
        if (IsAuthorityMode)
            ValidateAndApply(attackerId, victimId, attackId, attackerPos, facing);
        else
            RpcId(1, nameof(SubmitHitRpc), victimId, attackId, attackerPos, facing);
    }

    public void RequestBuff(float multiplier)
    {
        if (IsAuthorityMode)
            ApplyBuff(MyPeerId, multiplier);
        else
            RpcId(1, nameof(SubmitBuffRpc), multiplier);
    }

    /// <summary>Nightblade stealth (Vision 7): the server owns the state —
    /// cooldown, 5 s duration, break-on-attack with the +50% next hit.</summary>
    public void RequestStealth()
    {
        if (IsAuthorityMode)
            ApplyStealth(MyPeerId);
        else
            RpcId(1, nameof(SubmitStealthRpc));
    }

    /// <summary>Nightblade smoke bomb (Vision 7): the server validates throw
    /// range + cooldown, then broadcasts the zone so every peer builds it.</summary>
    public void RequestSmoke(Vector3 pos)
    {
        if (IsAuthorityMode)
            ValidateAndSpawnSmoke(MyPeerId, pos);
        else
            RpcId(1, nameof(SubmitSmokeRpc), pos);
    }

    /// <summary>Revenant soul ward (Vision 7): the server owns the absorb
    /// pool, its duration and the cooldown.</summary>
    public void RequestWard()
    {
        if (IsAuthorityMode)
            ApplyWard(MyPeerId);
        else
            RpcId(1, nameof(SubmitWardRpc));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitHitRpc(int victimId, int attackId, Vector3 attackerPos, Vector3 facing)
        => ValidateAndApply(Multiplayer.GetRemoteSenderId(), victimId, attackId,
            attackerPos, facing);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitBuffRpc(float multiplier)
        => ApplyBuff(Multiplayer.GetRemoteSenderId(), multiplier);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitStealthRpc()
        => ApplyStealth(Multiplayer.GetRemoteSenderId());

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitSmokeRpc(Vector3 pos)
        => ValidateAndSpawnSmoke(Multiplayer.GetRemoteSenderId(), pos);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitWardRpc()
        => ApplyWard(Multiplayer.GetRemoteSenderId());

    // --------------------------- server validation -------------------------

    private void ValidateAndApply(int attackerPeer, int victimId, int attackId,
        Vector3 attackerPos, Vector3 facing)
    {
        if (victimId == attackerPeer)
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: self-targeting");
            return;
        }
        // Harness bots request hits for ids the ENet socket knows nothing
        // about — only the SENDERS of real peer ids must be checked.
        if (attackerPeer != Multiplayer.GetRemoteSenderId() &&
            !(attackerPeer == MyPeerId && attackerPeer == 1) &&
            !(attackerPeer >= 500 && attackerPeer < 1000))
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: not the request sender");
            return;
        }
        if (!_targets.TryGetValue(victimId, out var victim))
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: unknown target");
            return;
        }
        if (!_hp.TryGetValue(victimId, out int hp) || hp <= 0)
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: target dead");
            return;
        }

        var atk = CombatTables.Get(attackId);
        double now = Time.GetTicksMsec() / 1000.0;

        // Nightblade smoke (Vision 7): hits OUT of or THROUGH the cloud are
        // blind — the attacker inside the zone can't see, the victim inside
        // it can't be seen.
        if (SmokeZone.AnyZoneContains(this, attackerPos))
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: attacker smoke-blind");
            return;
        }
        if (SmokeZone.AnyZoneContains(this, victim.CombatPosition))
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: victim smoke-blind");
            return;
        }

        var key = (attackerPeer, attackId);
        // Gear haste (loot slice 3, Vision 8): equipped haste affixes shrink
        // the SERVER-side interval floor for this peer (BALANCE.md) — 0.01 s
        // per point, capped at half speed. Remote-peer gear gap (NEXT 1):
        // the handshake-declared stats serve remote peers too.
        float hasteMult = 1f - Mathf.Min(CombatTables.HasteCapMultiplier,
            (float)(GearHasteOfPeer(attackerPeer) * CombatTables.HastePerPoint));
        if (_lastHitAt.TryGetValue(key, out double last) &&
            now - last < atk.MinInterval * hasteMult - 0.05)
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: cooldown " +
                   $"({now - last:0.00}s < {atk.MinInterval * hasteMult:0.00}s)");
            return;
        }

        // Ground-projected hitbox validated against the SERVER's own world.
        // For player victims this is their last REPORTED position (anti-cheat
        // hardening vs stale reports is a later task).
        Vector3 to = victim.CombatPosition - attackerPos;
        to.Y = 0f;
        float dist = to.Length();
        if (dist > atk.Range + RangeSlack)
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: range " +
                   $"{dist:0.00} > {atk.Range + RangeSlack:0.00}");
            return;
        }
        if (atk.Shape == AttackShape.Line)
        {
            // Ground line (bone spear): perpendicular distance from the
            // victim to the strip attackerPos -> attackerPos + facing*Range.
            var fwd = facing;
            fwd.Y = 0f;
            fwd = fwd.Normalized();
            float lateral = Mathf.Abs(to.Cross(fwd).Y);
            if (lateral > atk.Width * 0.5f + RangeSlack)
            {
                Reject($"hit victim={victimId} peer={attackerPeer}: outside " +
                       $"line (lateral {lateral:0.00} > {atk.Width * 0.5f + RangeSlack:0.00})");
                return;
            }
        }
        else if (dist > 0.001f &&
            Mathf.RadToDeg(facing.AngleTo(to.Normalized())) > atk.ArcDegrees * 0.5f)
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: outside " +
                   $"{atk.ArcDegrees:0}deg arc");
            return;
        }

        _lastHitAt[key] = now;
        // Cast relay (remote-avatar polish, NEXT 1): the swing/cast itself is
        // broadcast so remote puppets replay it. Validation (cooldown/arc/
        // line) has passed and the attack is REGISTERED here — the relay is
        // not gated on the damage math below (a fully warded hit still reads
        // as a cast on screen). The owning client already played its own
        // swing; CastRpc skips it.
        SendCast(attackerPeer, attackId, facing);
        // Nightblade stealth (Vision 7): the first attack breaks the state
        // and the server adds the +50% bonus to THAT hit. Sane-capped stack.
        float mult = BuffOf(attackerPeer, now);
        if (_stealthUntil.Remove(attackerPeer))
        {
            mult *= CombatTables.StealthBonus;
            SendStealthState(attackerPeer, false);
        }
        mult = Mathf.Min(mult, CombatTables.MaxTotalMultiplier);
        int dmg = Mathf.RoundToInt(atk.Damage * mult);

        // Revenant soul ward (Vision 7): the absorb pool eats damage BEFORE
        // HP — a fully absorbed hit leaves the victim untouched.
        if (dmg > 0 && _wards.TryGetValue(victimId, out float ward) && ward > 0f)
        {
            float absorbed = Mathf.Min(ward, dmg);
            dmg -= (int)absorbed;
            ward -= absorbed;
            if (ward <= 0f)
            {
                _wards.Remove(victimId);
                _wardUntil.Remove(victimId);
            }
            else
            {
                _wards[victimId] = ward;
            }
            SendWardState(victimId, Mathf.Max(0f, ward));
            GD.Print($"AUTHORITY: ward victim={victimId} absorbed {absorbed:0} " +
                     $"({Mathf.Max(0f, ward):0} left)");
            if (dmg <= 0)
                return;   // fully absorbed: no HP change, no kill logic
        }
        int hpAfter = Mathf.Max(0, hp - dmg);
        bool killed = hpAfter == 0;
        _hp[victimId] = hpAfter;

        SendApplyHit(victimId, dmg, atk.Heavy, hpAfter, killed);
        if (atk.StunSeconds > 0f && !killed)
            SendTargetStunned(victimId, atk.StunSeconds);
        if (atk.RootSeconds > 0f && !killed)
            SendTargetRooted(victimId, atk.RootSeconds);

        // Revenant life drain (Vision 7): heal the attacker for a fraction
        // of the damage — server-owned, capped at max HP.
        if (atk.HealFraction > 0f && dmg > 0 &&
            _hp.TryGetValue(attackerPeer, out int ahp) && ahp < _maxHp[attackerPeer])
        {
            int heal = Mathf.RoundToInt(dmg * atk.HealFraction);
            int ahpAfter = Mathf.Min(_maxHp[attackerPeer], ahp + heal);
            if (ahpAfter > ahp)
            {
                _hp[attackerPeer] = ahpAfter;
                SendHealed(attackerPeer, ahpAfter);
                GD.Print($"AUTHORITY: drain heal attacker={attackerPeer} +{ahpAfter - ahp} hp={ahpAfter}");
            }
        }
        if (killed)
        {
            SendKillFeed($"{PeerName(attackerPeer)} slew {victim.DisplayName}");
            _respawnAt[victimId] = now + RespawnDelay;
            AwardKillXp(attackerPeer, victimId, victim);
            SpawnLootDrop(victimId, victim.CombatPosition,
                _kills.TryGetValue(attackerPeer, out int kk) ? kk : 0);
            // Vision 8 ranking: PvP kills resolve a duel Elo result (server
            // -> central). Real peers are in _peers — dummies (id >= 1000) and
            // unregistered ids never rank (a huge ENet peer id is >= 1000:
            // the old victimId < 1000 check silently skipped every real PvP
            // kill — found live).
            if (_peers.ContainsKey(victimId))
                ReportMmr(attackerPeer, victimId, victim);
            GD.Print($"AUTHORITY: KILL attacker={PeerName(attackerPeer)} " +
                     $"victim={victim.DisplayName}");
        }
        GD.Print($"AUTHORITY: hit victim={victimId} attack={attackId} " +
                 $"dmg={dmg} hp={hpAfter}/{_maxHp[victimId]} peer={attackerPeer} " +
                 (mult > 1f ? $"mult={mult:0.00}" : ""));
    }

    private void Reject(string reason) => GD.Print($"AUTHORITY REJECT: {reason}");

    /// <summary>Vision 8: XP from kills — server-owned number (clients never
    /// compute rewards). Dummies award less than players; the award is
    /// broadcast to every peer so the LOCAL player's mirror + HUD stay live.</summary>
    private void AwardKillXp(int attackerPeer, int victimId, ICombatTarget victim)
    {
        // A peer (player or bot) is a ranked combatant; world targets
        // (dummies, id >= 1000, never in _peers) give the smaller dummy XP.
        // The old victimId >= 1000 check misread real ENet peer ids as dummies.
        int xp = _peers.ContainsKey(victimId) ? Progression.XpPerPlayerKill : Progression.XpPerDummyKill;
        _kills[attackerPeer] = _kills.TryGetValue(attackerPeer, out int k) ? k + 1 : 1;
        _xp[attackerPeer] = _xp.TryGetValue(attackerPeer, out int x) ? x + xp : xp;
        SendProgress(attackerPeer, _kills[attackerPeer], _xp[attackerPeer]);
        GD.Print($"AUTHORITY: XP attacker={attackerPeer} +{xp} xp (kills={_kills[attackerPeer]})");
    }

    private void SendProgress(int peerId, int kills, int xp)
    {
        if (Networked)
            Rpc(nameof(ProgressRpc), peerId, kills, xp);
        else
            ProgressRpc(peerId, kills, xp);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ProgressRpc(int peerId, int kills, int xp)
    {
        if (_targets.TryGetValue(peerId, out var target))
            target.OnProgress(kills, xp);
    }

    // ------------------------------ ranking (Vision 8) -----------------------

    /// <summary>Duel resolved: the MATCH SERVER reports Elo to central (its
    /// token authorizes it) and broadcasts the applied result to every peer.
    /// Only PvP kills report — the training dummy is not a ranked opponent.
    /// Characters without a central id (offline/test, harness bots) skip.
    /// </summary>
    private void ReportMmr(int winnerPeer, int loserPeer, ICombatTarget loser)
    {
        if (_mmrReported.Contains((winnerPeer, loserPeer)))
            return;                          // one Elo per resolved duel
        _mmrReported.Add((winnerPeer, loserPeer));

        long winnerChar = _peers.TryGetValue(winnerPeer, out var w) ? w.CharacterId : 0;
        long loserChar = _peers.TryGetValue(loserPeer, out var l) ? l.CharacterId : 0;
        if (winnerChar <= 0 || loserChar <= 0)
        {
            GD.Print($"AUTHORITY: MMR skipped ({PeerName(winnerPeer)} vs {loser.DisplayName}) " +
                     "— no central character on one side (offline/test)");
            return;
        }
        if (ServerToken.Length == 0)
        {
            // A realm that never registered (listen-host client flow) cannot
            // report: log it — Elo only flows from token-holding realms.
            GD.Print("AUTHORITY: MMR skipped — realm has no central server token");
            return;
        }

        var reporter = new CentralClient { Name = "MmrReporter" };
        AddChild(reporter);
        _ = ReportMmrAsync(reporter, winnerChar, loserChar);
    }

    private async System.Threading.Tasks.Task ReportMmrAsync(CentralClient reporter,
        long winnerChar, long loserChar)
    {
        var result = await reporter.ReportMmr(new MmrReport(
            ServerToken, 0, winnerChar, loserChar, 0, 0));
        reporter.QueueFree();
        if (result is null)
        {
            GD.PrintErr("AUTHORITY: MMR REPORT FAILED — central unreachable or rejected");
            return;
        }
        SendMmrResult(result);
        GD.Print($"AUTHORITY: MMR +{result.WinnerDelta} winner_char={result.WinnerCharacterId} " +
                 $"-> {result.WinnerMmr} ({result.WinnerTier}) | " +
                 $"{result.LoserDelta} loser_char={result.LoserCharacterId} " +
                 $"-> {result.LoserMmr} ({result.LoserTier})");
    }

    private void SendMmrResult(MmrResult result)
    {
        if (Networked)
            Rpc(nameof(MmrRpc), result.WinnerCharacterId, result.LoserCharacterId,
                result.WinnerMmr, result.LoserMmr, result.WinnerDelta, result.LoserDelta,
                result.WinnerTier, result.LoserTier);
        else
            MmrRpc(result.WinnerCharacterId, result.LoserCharacterId, result.WinnerMmr,
                result.LoserMmr, result.WinnerDelta, result.LoserDelta,
                result.WinnerTier, result.LoserTier);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void MmrRpc(long winnerCharacterId, long loserCharacterId,
        int winnerMmr, int loserMmr, int winnerDelta, int loserDelta,
        string winnerTier, string loserTier)
    {
        // Each peer mirrors ONLY ITS OWN session MMR (the mirror lives in the
        // session; remote peers' MMR stays on central until the card refresh).
        ProgressionSession.OnMmr(winnerCharacterId, loserCharacterId,
            winnerMmr, loserMmr, winnerDelta, loserDelta, winnerTier, loserTier);
        EmitSignal(SignalName.MmrResolved, winnerCharacterId, loserCharacterId,
            winnerMmr, loserMmr, winnerDelta, loserDelta, winnerTier, loserTier);
    }

    // ------------------------------ loot (Vision 8) -------------------------

    /// <summary>The authority rolls a loot shard on every kill (item level
    /// scales with the killer's match kills). Seeded + broadcast so every
    /// peer builds the identical drop node at the victim's position.</summary>
    private void SpawnLootDrop(int victimId, Vector3 position, int killerKills)
    {
        int dropId = _nextDropId++;
        ulong seed = (ulong)Time.GetTicksMsec() ^ ((ulong)(uint)dropId * 0x9E3779B97F4A7C15UL);
        int ilvl = 1 + killerKills / 3;
        SendLootDrop(dropId, seed, ilvl, position);
        GD.Print($"AUTHORITY: loot drop id={dropId} seed={seed} ilvl={ilvl} at {position}");
    }

    private void SendLootDrop(int dropId, ulong seed, int itemLevel, Vector3 pos)
    {
        if (Networked)
            Rpc(nameof(LootDropRpc), dropId, seed, itemLevel, pos);
        else
            LootDropRpc(dropId, seed, itemLevel, pos);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void LootDropRpc(int dropId, ulong seed, int itemLevel, Vector3 pos)
    {
        var arena = GetParent().GetNodeOrNull<Node3D>("Arena");
        if (arena is null)
            return;
        var drop = new LootDrop
        {
            Name = $"LootDrop{dropId}",
            DropId = dropId,
            Seed = seed,
            ItemLevel = itemLevel,
            Position = pos + new Vector3(0, 0.05f, 0),
        };
        arena.AddChild(drop, forceReadableName: true);
    }

    /// <summary>Client asks to pick up a drop; the server validates proximity
    /// against ITS OWN last known player position before awarding.</summary>
    public void RequestPickup(int dropId)
    {
        if (IsAuthorityMode)
            ValidatePickup(MyPeerId, dropId);
        else
            RpcId(1, nameof(SubmitPickupRpc), dropId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitPickupRpc(int dropId)
        => ValidatePickup(Multiplayer.GetRemoteSenderId(), dropId);

    private void ValidatePickup(int peer, int dropId)    {
        var arena = GetParent().GetNodeOrNull<Node3D>("Arena");
        var drop = arena?.GetNodeOrNull<World.LootDrop>($"LootDrop{dropId}");
        if (drop is null || drop.IsTaken)
        {
            Reject($"pickup drop={dropId} peer={peer}: drop gone");
            return;
        }
        Vector3 playerPos = _peers.TryGetValue(peer, out var info)
            ? info.Position
            : Vector3.Zero;
        float dist = playerPos.DistanceTo(drop.GlobalPosition);
        if (dist > World.LootDrop.PickupRadius + 0.5f)
        {
            Reject($"pickup drop={dropId} peer={peer}: too far ({dist:0.00} m)");
            return;
        }
        drop.MarkTaken();
        SendLootTaken(dropId, peer);
        // Loot slice 2 (Vision 8): ward affixes on freshly picked-up gear
        // grow the peer's absorb pool (server-owned; stacks with the soul
        // ward — both run through the same _wards dictionary).
        int gearWard = GearWardOfPeer(peer);
        if (gearWard > 0 && !_wards.ContainsKey(peer))
        {
            _wards[peer] = gearWard;
            _wardUntil.Remove(peer);            // gear ward: no expiry
            SendWardState(peer, gearWard);
        }
    }

    private void SendLootTaken(int dropId, int peer)
    {
        if (Networked)
            Rpc(nameof(LootTakenRpc), dropId, peer);
        else
            LootTakenRpc(dropId, peer);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void LootTakenRpc(int dropId, int peer)
    {
        var arena = GetParent().GetNodeOrNull<Node3D>("Arena");
        var drop = arena?.GetNodeOrNull<World.LootDrop>($"LootDrop{dropId}");
        if (drop is not null)
            drop.QueueFree();
        if (peer == MyPeerId)
        {
            // Rebuild the item from the drop's seed (same math as _Ready).
            var rng = new RandomNumberGenerator { Seed = drop?.Seed ?? 0 };
            var item = ItemGenerator.Generate(rng, drop?.ItemLevel ?? 1);
            ProgressionSession.AddLoot(item);
            EmitSignal(SignalName.LootGranted, item.Name, ItemGenerator.RarityLabel(item.Rarity));
            GD.Print($"LOOT TAKEN: {item.Name} ({ItemGenerator.RarityLabel(item.Rarity)}, ilvl {item.ItemLevel}, " +
                     $"{ItemGenerator.AffixList(item)}) — session loot {ProgressionSession.Loot.Count}");
        }
    }

    /// <summary>Client-side equip request (Vision 8): the server just
    /// REBROADCASTS it so every peer retints the puppet; the item itself is
    /// session data, not combat state.</summary>
    public void RequestEquip()
    {
        if (Networked)
            Rpc(nameof(EquipRpc), MyPeerId);
        else
            EquipRpc(MyPeerId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void EquipRpc(int peerId)
        => EmitSignal(SignalName.EquipmentChanged, peerId);

    private void ApplyBuff(int peer, float multiplier)
    {
        // Sane-cap validation: clients may not grant themselves big buffs.
        float clamped = Mathf.Clamp(multiplier, 1f, MaxBuffMultiplier);
        double now = Time.GetTicksMsec() / 1000.0;
        _buffs[peer] = (clamped, now + BuffDuration);
        GD.Print($"AUTHORITY: buff peer={peer} mult={clamped:0.00} for {BuffDuration:0}s");
    }

    private float BuffOf(int peer, double now) =>
        _buffs.TryGetValue(peer, out var b) && b.Until > now ? b.Mult : 1f;

    private void ApplyWard(int peer)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_lastWardAt.TryGetValue(peer, out double last) &&
            now - last < CombatTables.SoulWardCooldown - 0.05)
        {
            Reject($"ward peer={peer}: cooldown " +
                   $"({now - last:0.00}s < {CombatTables.SoulWardCooldown:0.00}s)");
            return;
        }
        // Gear ward (loot slice 2) sits UNDER the soul ward: the kit cast
        // replaces the pool while active; the gear pool returns when the
        // kit ward expires (see _Process ward-expiry branch).
        _lastWardAt[peer] = now;
        _wards[peer] = CombatTables.SoulWardAbsorb + peerGearWard(peer);
        _wardUntil[peer] = now + CombatTables.SoulWardDuration;
        SendWardState(peer, _wards[peer]);
        GD.Print($"AUTHORITY: ward peer={peer} absorbs {_wards[peer]:0} " +
                 $"for {CombatTables.SoulWardDuration:0}s");
    }

    /// <summary>Gear-ward component of a peer's absorb pool (loot slice 2:
    /// ward affixes). Remote-peer gear gap (NEXT 1): remote peers contribute
    /// their handshake-declared ward too, not just the local session.</summary>
    private int peerGearWard(int peer) => GearWardOfPeer(peer);

    private void SendWardState(int peerId, float amount)
    {
        if (Networked)
            Rpc(nameof(WardStateRpc), peerId, amount);
        else
            WardStateRpc(peerId, amount);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void WardStateRpc(int peerId, float amount)
    {
        if (_targets.TryGetValue(peerId, out var target))
            target.OnWard(amount);
    }

    private void SendHealed(int peerId, int hpAfter)
    {
        if (Networked)
            Rpc(nameof(HealedRpc), peerId, hpAfter);
        else
            HealedRpc(peerId, hpAfter);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HealedRpc(int peerId, int hpAfter)
    {
        if (_targets.TryGetValue(peerId, out var target))
            target.OnHealed(hpAfter);
    }

    private void ApplyStealth(int peer)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_lastStealthAt.TryGetValue(peer, out double last) &&
            now - last < CombatTables.StealthCooldown - 0.05)
        {
            Reject($"stealth peer={peer}: cooldown " +
                   $"({now - last:0.00}s < {CombatTables.StealthCooldown:0.00}s)");
            return;
        }
        _lastStealthAt[peer] = now;
        _stealthUntil[peer] = now + CombatTables.StealthDuration;
        SendStealthState(peer, true);
        GD.Print($"AUTHORITY: stealth peer={peer} for {CombatTables.StealthDuration:0}s " +
                 $"(next hit x{CombatTables.StealthBonus:0.0})");
    }
    private void ValidateAndSpawnSmoke(int peer, Vector3 pos)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_lastSmokeAt.TryGetValue(peer, out double last) &&
            now - last < CombatTables.SmokeCooldown - 0.05)
        {
            Reject($"smoke peer={peer}: cooldown " +
                   $"({now - last:0.00}s < {CombatTables.SmokeCooldown:0.00}s)");
            return;
        }
        Vector3 from = _peers.TryGetValue(peer, out var info)
            ? info.Position
            : Vector3.Zero;
        var to = pos - from;
        to.Y = 0f;
        if (to.Length() > CombatTables.SmokeThrowRange + RangeSlack)
        {
            Reject($"smoke peer={peer}: throw {to.Length():0.00} > " +
                   $"{CombatTables.SmokeThrowRange + RangeSlack:0.00}");
            return;
        }
        _lastSmokeAt[peer] = now;
        SendSmokeZone(pos);
        GD.Print($"AUTHORITY: smoke peer={peer} at {pos} for {CombatTables.SmokeDuration:0}s");
    }

    /// <summary>Stable display name for any combat id (killfeed + harness
    /// matrix): networked peers get their roster name, bots keep their own,
    /// the local player is "You" offline.</summary>
    private string PeerName(int peer)
    {
        if (_peers.TryGetValue(peer, out var info) && info.Approved)
            return $"{PlayerClassInfo.Label(PlayerClassInfo.FromId(info.ClassId))}#{peer}";
        if (_targets.TryGetValue(peer, out var target) && target is CombatBot bot)
            return bot.DisplayName;
        return Networked ? $"Warden#{peer}" : "You";
    }

    // ---------------------------- broadcasts -------------------------------
    // Authority -> every peer. CallLocal = true runs them on the host too;
    // offline, the senders invoke the bodies directly (single local peer).

    private void SendApplyHit(int victimId, int amount, bool heavy, int hpAfter, bool killed)
    {
        if (Networked)
            Rpc(nameof(ApplyHitRpc), victimId, amount, heavy, hpAfter, killed);
        else
            ApplyHitRpc(victimId, amount, heavy, hpAfter, killed);
    }

    private void SendCast(int peerId, int attackId, Vector3 facing)
    {
        if (Networked)
            Rpc(nameof(CastRpc), peerId, attackId, facing);
        else
            CastRpc(peerId, attackId, facing);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void CastRpc(int peerId, int attackId, Vector3 facing)
    {
        if (!_targets.TryGetValue(peerId, out var target))
            return;
        // The owning client already played its own swing locally (the chains
        // call PlayAttackAnim before requesting) — replaying it would double-
        // play. Bots animate themselves; RemoteAvatar puppets are the only
        // bodies that need the relayed cast.
        if (target is PlayerController pc && pc.PeerId == peerId)
            return;
        if (target is RemoteAvatar avatar)
            avatar.PlayRemoteCast(attackId, facing);
    }

    private void SendTargetStunned(int victimId, float seconds)
    {
        if (Networked)
            Rpc(nameof(TargetStunnedRpc), victimId, seconds);
        else
            TargetStunnedRpc(victimId, seconds);
    }

    private void SendTargetRooted(int victimId, float seconds)
    {
        if (Networked)
            Rpc(nameof(TargetRootedRpc), victimId, seconds);
        else
            TargetRootedRpc(victimId, seconds);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TargetRootedRpc(int victimId, float seconds)
    {
        if (_targets.TryGetValue(victimId, out var victim))
            victim.OnRooted(seconds);
    }

    private void SendTargetRespawned(int victimId, int hpAfter)
    {
        if (Networked)
            Rpc(nameof(TargetRespawnedRpc), victimId, hpAfter);
        else
            TargetRespawnedRpc(victimId, hpAfter);
    }

    private void SendKillFeed(string text)
    {
        if (Networked)
            Rpc(nameof(KillFeedRpc), text);
        else
            KillFeedRpc(text);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ApplyHitRpc(int victimId, int amount, bool heavy, int hpAfter, bool killed)
    {
        if (!_targets.TryGetValue(victimId, out var victim))
            return;
        victim.OnHitApplied(amount, heavy, hpAfter);
        if (killed)
            victim.OnKilled();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TargetStunnedRpc(int victimId, float seconds)
    {
        if (_targets.TryGetValue(victimId, out var victim))
            victim.OnStunned(seconds);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TargetRespawnedRpc(int victimId, int hpAfter)
    {
        Vector3 spawn = _respawnPos.TryGetValue(victimId, out var pos)
            ? pos
            : SpawnPoints[0];
        if (_targets.TryGetValue(victimId, out var victim))
            victim.OnRespawned(hpAfter, spawn);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void KillFeedRpc(string text) => EmitSignal(SignalName.KillFeed, text);

    private void SendStealthState(int peerId, bool stealthed)
    {
        if (Networked)
            Rpc(nameof(StealthStateRpc), peerId, stealthed);
        else
            StealthStateRpc(peerId, stealthed);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StealthStateRpc(int peerId, bool stealthed)
    {
        if (_targets.TryGetValue(peerId, out var target))
            target.OnStealthed(stealthed);
    }

    private void SendSmokeZone(Vector3 pos)
    {
        if (Networked)
            Rpc(nameof(SmokeZoneRpc), pos);
        else
            SmokeZoneRpc(pos);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SmokeZoneRpc(Vector3 pos)
    {
        // Identical zone node on every peer (CallLocal covers the server):
        // clients render it + use it for the blind overlay; the server's copy
        // is the blind reference for hit validation.
        var zone = new SmokeZone { Name = $"SmokeZone{_nextSmokeZone++}", Position = pos };
        GetParent().AddChild(zone, forceReadableName: true);
    }

    // --------------------------- timers + beats ----------------------------

    public override void _Process(double delta)
    {
        // Loot pickup proximity (Vision 8): walk over a shard -> pick it up
        // through the server-validated path (offline: local authority).
        if (_lootScanAccum >= 0.25)
        {
            _lootScanAccum = 0;
            var arena = GetParent().GetNodeOrNull<Node3D>("Arena");
            var ownBody = _targets.TryGetValue(MyPeerId, out var ownTarget)
                ? ownTarget as Node3D
                : null;
            if (arena is not null && ownBody is not null && !ownTarget!.IsDead)
            {
                foreach (var child in arena.GetChildren())
                {
                    if (child is World.LootDrop dropNode && !dropNode.IsTaken &&
                        ownBody.GlobalPosition.DistanceTo(dropNode.GlobalPosition)
                            <= World.LootDrop.PickupRadius)
                    {
                        RequestPickup(dropNode.DropId);
                        break;   // one per scan; the next pass takes the rest
                    }
                }
            }
        }
        _lootScanAccum += delta;

        if (IsAuthorityMode && _respawnAt.Count > 0)
        {
            double now = Time.GetTicksMsec() / 1000.0;
            List<int> due = new();
            foreach (var (id, at) in _respawnAt)
                if (now >= at)
                    due.Add(id);
            foreach (int id in due)
            {
                _respawnAt.Remove(id);
                if (!_targets.ContainsKey(id))
                    continue;
                _hp[id] = _maxHp[id];
                SendTargetRespawned(id, _maxHp[id]);
                GD.Print($"AUTHORITY: respawn victim={id} hp={_maxHp[id]}");
            }
        }

        // Nightblade stealth expiry (Vision 7): 5 s elapse, the ghost returns
        // without needing an attack to break it.
        if (IsAuthorityMode && _stealthUntil.Count > 0)
        {
            double now2 = Time.GetTicksMsec() / 1000.0;
            List<int> expired = new();
            foreach (var (id, until) in _stealthUntil)
                if (now2 >= until)
                    expired.Add(id);
            foreach (int id in expired)
            {
                _stealthUntil.Remove(id);
                SendStealthState(id, false);
                GD.Print($"AUTHORITY: stealth expired peer={id}");
            }
        }

        // Soul ward expiry: the pool dies unspent after its duration.
        if (IsAuthorityMode && _wardUntil.Count > 0)
        {
            double now3 = Time.GetTicksMsec() / 1000.0;
            List<int> wardsGone = new();
            foreach (var (id, until) in _wardUntil)
                if (now3 >= until)
                    wardsGone.Add(id);
            foreach (int id in wardsGone)
            {
                _wardUntil.Remove(id);
                _wards.Remove(id);
                SendWardState(id, 0f);
                GD.Print($"AUTHORITY: ward expired peer={id}");
                // Loot slice 2 (Vision 8): the gear ward returns after the
                // kit ward pool expires (it only replaced it while active).
                // Remote-peer gear gap (NEXT 1): remote peers restore their
                // declared gear pool too.
                int gearWard = GearWardOfPeer(id);
                if (gearWard > 0)
                {
                    _wards[id] = gearWard;
                    SendWardState(id, gearWard);
                    GD.Print($"AUTHORITY: gear ward restored peer={id} ({gearWard})");
                }
            }
        }

        // Client -> server position report (10 Hz, unreliable).
        if (Networked && !IsAuthorityMode &&
            _targets.TryGetValue(MyPeerId, out var self) && self is PlayerController pc)
        {
            _positionAccum += delta;
            if (_positionAccum >= PositionInterval)
            {
                _positionAccum = 0;
                RpcId(1, nameof(ReportPositionRpc), pc.GlobalPosition,
                    Mathf.DegToRad(pc.RotationDegrees.Y));
            }
        }

        // Server beat: roster + positions, the sync's log evidence.
        if (IsAuthorityMode && Networked && _peers.Count > 0)
        {
            _beatAccum += delta;
            if (_beatAccum >= BeatInterval)
            {
                _beatAccum = 0;
                var parts = new List<string>();
                foreach (var (id, info) in _peers)
                    parts.Add($"{id}@({info.Position.X:0.0},{info.Position.Z:0.0})" +
                              $"{(info.Approved ? "" : "?")}");
                GD.Print($"REALM: peers={_peers.Count} " + string.Join(" ", parts));
            }
        }
    }

    /// <summary>Rich state for the playtester runtime digest (mcp_watch).</summary>
    public Godot.Collections.Dictionary _mcp_state() => new()
    {
        ["kills"] = _kills.TryGetValue(MyPeerId, out int mk) ? mk : 0,
        ["xp"] = _xp.TryGetValue(MyPeerId, out int mx) ? mx : 0,
        ["authority"] = IsAuthorityMode,
        ["networked"] = Networked,
        ["peer_id"] = MyPeerId,
        ["peer_count"] = _peers.Count,
        ["target_count"] = _targets.Count,
        ["hp"] = string.Join(" ", _hp),
        ["pending_respawns"] = _respawnAt.Count,
        ["stealthed"] = string.Join(",", _stealthUntil.Keys),
        ["smoke_zones"] = GetTree().GetNodesInGroup("smoke_zone").Count,
        ["wards"] = string.Join(",", _wards.Keys),
    };
}
