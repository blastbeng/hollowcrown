using System.Collections.Generic;

namespace Hollowcrown.Combat;

/// <summary>
/// Server-owned combat numbers (Vision 2.3): damage, reach, arc, stun and the
/// server-side minimum interval per attack live HERE and are applied by
/// CombatAuthority. Clients never compute damage — their local exports only
/// gate VFX/costs/cooldown feel and must match this table (BALANCE.md is the
/// human-readable copy). Changing a number = editing this table, not code.
/// </summary>
public enum AttackId
{
    ChainLight = 1,    // warden chain, swing 1
    ChainMid = 2,      // warden chain, swing 2
    ChainFinisher = 3, // warden chain, heavy finisher
    ShieldBash = 4,    // warden kit, E
    DaggerLight = 5,   // nightblade chain, stab 1
    DaggerMid = 6,     // nightblade chain, stab 2
    DaggerFinisher = 7,// nightblade chain, heavy finisher
    BoneSpear = 8,     // revenant kit, Q — ground-line projectile
    GraveGrasp = 9,    // revenant kit, E — root circle
}

public enum AttackShape
{
    Arc,    // sector centered on the attacker (warden/nightblade)
    Line,   // strip from the attacker toward the aim point (bone spear)
}

public static class CombatTables
{
    public readonly record struct Attack(
        int Damage, bool Heavy, float Range, float ArcDegrees,
        float StunSeconds, float MinInterval,
        AttackShape Shape = AttackShape.Arc, float Width = 0f,
        float RootSeconds = 0f);

    // BALANCE.md: warden_chain (20/20/35, 2.4 m, 120 deg), warden_bash
    // (15, 3.2 m, 90 deg, 0.5 s stun, 6 s cd). MinInterval is the server's
    // anti-spam floor, slightly under the client-side cooldown.
    private static readonly Dictionary<int, Attack> Table = new()
    {
        [(int)AttackId.ChainLight] = new(20, false, 2.4f, 120f, 0f, 0.30f),
        [(int)AttackId.ChainMid] = new(20, false, 2.4f, 120f, 0f, 0.30f),
        [(int)AttackId.ChainFinisher] = new(35, true, 2.4f, 120f, 0f, 0.45f),
        [(int)AttackId.ShieldBash] = new(15, true, 3.2f, 90f, 0.5f, 5.5f),
        // BALANCE.md: nightblade_chain — fast light stabs, shorter reach,
        // tighter arc than the warden (Vision 7: "fast dual-dagger chain").
        [(int)AttackId.DaggerLight] = new(14, false, 2.0f, 100f, 0f, 0.18f),
        [(int)AttackId.DaggerMid] = new(14, false, 2.0f, 100f, 0f, 0.18f),
        [(int)AttackId.DaggerFinisher] = new(28, true, 2.0f, 100f, 0f, 0.35f),
        // BALANCE.md: revenant_kit — bone spear hits EVERYTHING on a 9 m,
        // 1.2 m wide ground line; grave grasp roots a 4.5 m circle for 1 s
        // (rooted bodies cannot move but can still fight, Vision 7).
        [(int)AttackId.BoneSpear] = new(18, true, 9f, 360f, 0f, 4.5f,
            AttackShape.Line, 1.2f),
        [(int)AttackId.GraveGrasp] = new(6, false, 4.5f, 360f, 0f, 8.5f,
            AttackShape.Arc, 0f, 1.0f),
    };

    public static Attack Get(int id) => Table.TryGetValue(id, out var atk)
        ? atk
        : new Attack(1, false, 2f, 90f, 0f, 0.5f);   // unknown id: near-harmless

    // ---- Nightblade kit (Vision 7; BALANCE.md: nightblade_kit) -----------
    public const float StealthDuration = 5f;         // breaks on attack
    public const float StealthBonus = 1.5f;          // next hit +50% (server-computed)
    public const float StealthCooldown = 12f;        // server-enforced per peer
    public const float SmokeRadius = 3.5f;           // blind zone radius
    public const double SmokeDuration = 6.0;         // seconds the zone lives
    public const float SmokeThrowRange = 10f;        // max throw distance
    public const float SmokeCooldown = 8f;           // server-enforced per peer

    /// <summary>Sane cap for the stealth + warcry stack (anti-cheat).</summary>
    public const float MaxTotalMultiplier = 1.75f;
}
