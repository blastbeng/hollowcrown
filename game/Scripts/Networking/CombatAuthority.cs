using System.Collections.Generic;
using Godot;
using Hollowcrown.Combat;

namespace Hollowcrown.Networking;

/// <summary>
/// Server-authoritative combat brain (Vision 2.3): the MATCH SERVER owns
/// every HP number. Clients only REQUEST hits (victim id + own position +
/// facing) and MIRROR broadcasts (damage numbers, stun, death, respawn,
/// killfeed); the server validates each hit against ITS OWN world — range,
/// arc, per-peer cooldown, sane buff caps — before applying anything
/// (Vision 4: minimal anti-cheat). Offline (no peer) the local peer IS the
/// authority, so single-player behaves identically to a networked realm.
///
/// RPC layout (Godot high-level multiplayer rules, docs-verified): every peer
/// runs the same scene tree, so this node lives at /root/Main/CombatAuthority
/// on the client AND the dedicated server and is added with
/// force_readable_name. Server-side HP/death/respawn state lives here; the
/// training dummy (and later players) only mirror what is broadcast.
/// </summary>
public partial class CombatAuthority : Node
{
    public const float RespawnDelay = 3.0f;          // BALANCE.md: dummy_respawn
    private const float RangeSlack = 0.35f;          // victim half-width tolerance
    private const float MaxBuffMultiplier = 1.25f;   // sane cap (anti-cheat)
    private const double BuffDuration = 10.0;        // BALANCE.md: warden_warcry

    [Signal] public delegate void KillFeedEventHandler(string text);

    private readonly Dictionary<int, ICombatTarget> _targets = new();
    private readonly Dictionary<int, int> _hp = new();
    private readonly Dictionary<int, int> _maxHp = new();
    private readonly Dictionary<(int Peer, int Attack), double> _lastHitAt = new();
    private readonly Dictionary<int, (float Mult, double Until)> _buffs = new();
    private readonly Dictionary<int, double> _respawnAt = new();
    private int _nextId = 1;

    /// <summary>True only with a REAL peer — the default OfflineMultiplayerPeer
    /// (always set, always id 1, server mode) counts as offline single-player.
    /// Offline, broadcast senders invoke the RPC bodies directly.</summary>
    public bool Networked => Multiplayer.HasMultiplayerPeer() &&
        Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;
    public bool IsAuthorityMode => !Networked || Multiplayer.IsServer();

    public static CombatAuthority? For(Node node) =>
        node.GetTree().GetFirstNodeInGroup("combat_authority") as CombatAuthority;

    public override void _Ready()
    {
        AddToGroup("combat_authority");
        GD.Print($"COMBAT AUTHORITY READY — networked={Networked} authority={IsAuthorityMode}");
    }

    // ------------------------------ registry -------------------------------

    public void Register(ICombatTarget target)
    {
        int id = _nextId++;
        _targets[id] = target;
        _hp[id] = target.MaxHp;
        _maxHp[id] = target.MaxHp;
        target.AssignCombatId(id);
        GD.Print($"AUTHORITY: registered \"{target.DisplayName}\" id={id} max_hp={target.MaxHp}");
    }

    public void Unregister(int id)
    {
        _targets.Remove(id);
        _hp.Remove(id);
        _maxHp.Remove(id);
        _respawnAt.Remove(id);
    }

    // --------------------------- client requests ---------------------------
    // Clients ask; the server decides. No local state is touched here.

    public void RequestHit(int victimId, int attackId, Vector3 attackerPos, Vector3 facing)
    {
        if (IsAuthorityMode)
            ValidateAndApply(1, victimId, attackId, attackerPos, facing);
        else
            RpcId(1, nameof(SubmitHitRpc), victimId, attackId, attackerPos, facing);
    }

    public void RequestBuff(float multiplier)
    {
        if (IsAuthorityMode)
            ApplyBuff(1, multiplier);
        else
            RpcId(1, nameof(SubmitBuffRpc), multiplier);
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

    // --------------------------- server validation -------------------------

    private void ValidateAndApply(int attackerPeer, int victimId, int attackId,
        Vector3 attackerPos, Vector3 facing)
    {
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

        var key = (attackerPeer, attackId);
        if (_lastHitAt.TryGetValue(key, out double last) &&
            now - last < atk.MinInterval - 0.05)
        {
            Reject($"hit victim={victimId} peer={attackerPeer}: cooldown " +
                   $"({now - last:0.00}s < {atk.MinInterval:0.00}s)");
            return;
        }

        // Ground-projected hitbox validated against the SERVER's own world.
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
        int dmg = Mathf.RoundToInt(atk.Damage * BuffOf(attackerPeer, now));
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
                 $"dmg={dmg} hp={hpAfter}/{_maxHp[victimId]} peer={attackerPeer}");
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
        if (_targets.TryGetValue(victimId, out var victim))
            victim.OnRespawned(hpAfter);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void KillFeedRpc(string text) => EmitSignal(SignalName.KillFeed, text);

    // ------------------------------ respawn --------------------------------

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
    }

    /// <summary>Rich state for the playtester runtime digest (mcp_watch).</summary>
    public Godot.Collections.Dictionary _mcp_state() => new()
    {
        ["authority"] = IsAuthorityMode,
        ["networked"] = Networked,
        ["target_count"] = _targets.Count,
        ["hp"] = string.Join(" ", _hp),
        ["pending_respawns"] = _respawnAt.Count,
    };
}
