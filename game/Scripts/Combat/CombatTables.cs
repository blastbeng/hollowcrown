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
}

public static class CombatTables
{
    public readonly record struct Attack(
        int Damage, bool Heavy, float Range, float ArcDegrees,
        float StunSeconds, float MinInterval);

    // BALANCE.md: warden_chain (20/20/35, 2.4 m, 120 deg), warden_bash
    // (15, 3.2 m, 90 deg, 0.5 s stun, 6 s cd). MinInterval is the server's
    // anti-spam floor, slightly under the client-side cooldown.
    private static readonly Dictionary<int, Attack> Table = new()
    {
        [(int)AttackId.ChainLight] = new(20, false, 2.4f, 120f, 0f, 0.30f),
        [(int)AttackId.ChainMid] = new(20, false, 2.4f, 120f, 0f, 0.30f),
        [(int)AttackId.ChainFinisher] = new(35, true, 2.4f, 120f, 0f, 0.45f),
        [(int)AttackId.ShieldBash] = new(15, true, 3.2f, 90f, 0.5f, 5.5f),
    };

    public static Attack Get(int id) => Table.TryGetValue(id, out var atk)
        ? atk
        : new Attack(1, false, 2f, 90f, 0f, 0.5f);   // unknown id: near-harmless
}
