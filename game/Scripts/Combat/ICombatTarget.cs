using Godot;

namespace Hollowcrown.Combat;

/// <summary>
/// Anything the match server can damage (Vision 2.3). The AUTHORITY owns HP
/// numbers; this interface is how results are mirrored onto every peer and
/// how the server reads world state for hit validation. Implementations must
/// never compute damage themselves.
/// </summary>
public interface ICombatTarget
{
    /// <summary>Id assigned by CombatAuthority at registration (identical
    /// arena build order on every peer keeps ids in sync across the net).</summary>
    int CombatId { get; }

    int MaxHp { get; }

    /// <summary>Mirrored authoritative HP (for UI/visuals only — never a
    /// gameplay decision input on clients).</summary>
    int Hp { get; }

    /// <summary>Mirrored death state.</summary>
    bool IsDead { get; }
    Vector3 CombatPosition { get; }
    string DisplayName { get; }

    void AssignCombatId(int id);

    /// <summary>Authority broadcast: damage applied (runs on every peer).</summary>
    void OnHitApplied(int amount, bool heavy, int hpAfter);

    /// <summary>Authority broadcast: control effect (stun) applied.</summary>
    void OnStunned(float seconds);

    /// <summary>Authority broadcast: root applied (cannot move, CAN still
    /// fight — unlike stun).</summary>
    void OnRooted(float seconds);

    /// <summary>Authority broadcast: stealth state changed (nightblade).
    /// Targets that can't stealth ignore it.</summary>
    void OnStealthed(bool stealthed);

    /// <summary>Authority broadcast: HP reached zero — death visuals.</summary>
    void OnKilled();

    /// <summary>Authority broadcast: back at full HP, placed at the target's
    /// spawn point (players teleport; static targets ignore the position).</summary>
    void OnRespawned(int hpAfter, Vector3 spawnPos);
}
