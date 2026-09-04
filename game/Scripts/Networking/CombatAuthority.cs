using System.Collections.Generic;
using Godot;
using Hollowcrown.Combat;
using Hollowcrown.Player;

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

    /// <summary>Password the NEXT outbound connection presents at handshake
    /// (set by ServerBrowser.Join or the --join launch flag before dialing).</summary>
    public static string PendingPassword = "";

    /// <summary>Class id the NEXT outbound connection declares at handshake
    /// ("warden"/"nightblade"/"revenant") — the server needs it for display
    /// names and so every peer spawns the right enemy model variant.</summary>
    public static string PendingClass = "warden";

    private sealed class PeerInfo
    {
        public bool Approved;
        public int SpawnIndex;
        public string ClassId = "warden";
        public Vector3 Position;
        public float Yaw;
        public ICombatTarget? Target;
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
        public int MaxHp => PlayerMaxHp;
        public int Hp => _auth._hp.TryGetValue(CombatId, out int hp) ? hp : MaxHp;
        public bool IsDead => Hp <= 0;
        public Vector3 CombatPosition => _auth._peers.TryGetValue(CombatId, out var info)
            ? info.Position
            : Vector3.Zero;
        public void AssignCombatId(int id) { }
        public void OnHitApplied(int amount, bool heavy, int hpAfter) { }
        public void OnStunned(float seconds) { }
        public void OnStealthed(bool stealthed) { }
        public void OnKilled() { }
        public void OnRespawned(int hpAfter, Vector3 spawnPos) { }
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
    private int _nextSmokeZone;
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
        RpcId(1, nameof(HandshakeRpc), PendingPassword, PendingClass);
        GD.Print("REALM: handshake sent — awaiting approval");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HandshakeRpc(string password, string classId)
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
        _hp[peer] = PlayerMaxHp;
        _maxHp[peer] = PlayerMaxHp;
        _respawnPos[peer] = spawn;
        info.Position = spawn;
        SendSpawnPlayer(peer, spawn, name, info.ClassId);
        GD.Print($"REALM: peer {peer} approved ({info.ClassId}) — spawns at {spawn}");

        // Catch the new peer up with everyone already approved in the realm
        // (late joiners miss earlier broadcast spawns).
        foreach (var (otherId, other) in _peers)
        {
            if (otherId == peer || !other.Approved)
                continue;
            RpcId(peer, nameof(SpawnPlayerRpc), otherId,
                SpawnPoints[other.SpawnIndex],
                $"{PlayerClassInfo.Label(PlayerClassInfo.FromId(other.ClassId))}#{otherId}",
                other.ClassId);
        }
    }

    private void SendSpawnPlayer(int peerId, Vector3 spawnPos, string displayName, string classId)
    {
        if (Networked)
            Rpc(nameof(SpawnPlayerRpc), peerId, spawnPos, displayName, classId);
        else
            SpawnPlayerRpc(peerId, spawnPos, displayName, classId);
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
    private void SpawnPlayerRpc(int peerId, Vector3 spawnPos, string displayName, string classId)
    {
        var arena = GetParent().GetNodeOrNull<Node3D>("Arena");

        if (peerId == MyPeerId)
        {
            // My spawn approval: reposition the local warden + re-register
            // under the real ENet peer id (boot-time registration used id 1).
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
            if (_peers.TryGetValue(peerId, out var info))
                info.Target = avatar;
            _targets[peerId] = avatar;
            _hp[peerId] = PlayerMaxHp;
            _maxHp[peerId] = PlayerMaxHp;
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
        _hp[peerId] = PlayerMaxHp;
        _maxHp[peerId] = PlayerMaxHp;
        _respawnPos[peerId] = SpawnPoints[0];
        player.AssignCombatId(peerId);
        GD.Print($"AUTHORITY: local warden registered id={peerId} max_hp={PlayerMaxHp}");
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

    // --------------------------- server validation -------------------------

    private void ValidateAndApply(int attackerPeer, int victimId, int attackId,
        Vector3 attackerPos, Vector3 facing)
    {
        if (victimId == attackerPeer)
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: self-targeting");
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
        if (_lastHitAt.TryGetValue(key, out double last) &&
            now - last < atk.MinInterval - 0.05)
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: cooldown " +
                   $"({now - last:0.00}s < {atk.MinInterval:0.00}s)");
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
        if (dist > 0.001f &&
            Mathf.RadToDeg(facing.AngleTo(to.Normalized())) > atk.ArcDegrees * 0.5f)
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: outside " +
                   $"{atk.ArcDegrees:0}deg arc");
            return;
        }

        _lastHitAt[key] = now;
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
        int hpAfter = Mathf.Max(0, hp - dmg);
        bool killed = hpAfter == 0;
        _hp[victimId] = hpAfter;

        SendApplyHit(victimId, dmg, atk.Heavy, hpAfter, killed);
        if (atk.StunSeconds > 0f && !killed)
            SendTargetStunned(victimId, atk.StunSeconds);
        if (killed)
        {
            SendKillFeed($"{PeerName(attackerPeer)} slew {victim.DisplayName}");
            _respawnAt[victimId] = now + RespawnDelay;
        }
        GD.Print($"AUTHORITY: hit victim={victimId} attack={attackId} " +
                 $"dmg={dmg} hp={hpAfter}/{_maxHp[victimId]} peer={attackerPeer} " +
                 (mult > 1f ? $"mult={mult:0.00}" : ""));
    }

    private void Reject(string reason) => GD.Print($"AUTHORITY REJECT: {reason}");

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

    private string PeerName(int peer) => Networked ? $"Warden#{peer}" : "You";

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

    private void SendTargetStunned(int victimId, float seconds)
    {
        if (Networked)
            Rpc(nameof(TargetStunnedRpc), victimId, seconds);
        else
            TargetStunnedRpc(victimId, seconds);
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
        ["authority"] = IsAuthorityMode,
        ["networked"] = Networked,
        ["peer_id"] = MyPeerId,
        ["peer_count"] = _peers.Count,
        ["target_count"] = _targets.Count,
        ["hp"] = string.Join(" ", _hp),
        ["pending_respawns"] = _respawnAt.Count,
        ["stealthed"] = string.Join(",", _stealthUntil.Keys),
        ["smoke_zones"] = GetTree().GetNodesInGroup("smoke_zone").Count,
    };
}
