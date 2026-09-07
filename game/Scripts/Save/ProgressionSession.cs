using System.Collections.Generic;
using Godot;
using Hollowcrown.Shared;

namespace Hollowcrown.Save;

/// <summary>
/// Session-scoped progression state (static: the arena builds long after the
/// menu screens are gone). The selected character flows from CharacterSelect
/// (card click); match stats are written by Main when the realm is left, and
/// are what the results screen shows and the central save reports.
///
/// Loot slice 2 (Vision 8): the session owns the whole BAG as ItemGenerator
/// items (not just names) and which items are equipped. Equipped affixes
/// feed the derived-stat helpers here; players and the SERVER both read
/// them (server-owned combat numbers stay server-computed, Vision 2.3).
/// On save, Main serializes bag + equip state into the central gear_json.
/// </summary>
public static class ProgressionSession
{
    public static int CharacterId { get; private set; }
    public static string CharacterName { get; private set; } = "";
    public static string ClassId { get; private set; } = "warden";
    public static int BaseXp { get; private set; }          // central XP at pick time
    public static int MatchKills { get; set; }
    public static int MatchXp { get; set; }

    /// <summary>Ranking (Vision 8): MMR snapshot captured at realm entry
    /// (the central value at card pick) — the results screen shows the delta
    /// against it. -1 = no central character was selected.</summary>
    public static int BaseMmr { get; private set; } = -1;

    /// <summary>Ranking (Vision 8): the duel result this session OWNED (this
    /// client's character was the winner or the loser), mirrored from the
    /// authority broadcast; the results screen reads it.</summary>
    public static int? MmrAfter { get; private set; }
    public static int? MmrDelta { get; private set; }
    public static string MmrTier { get; private set; } = "";

    /// <summary>Loot picked up this match, as FULL items (name + rarity +
    /// ilvl + affixes + slot) — the inventory panel, equipment model and the
    /// gear_json serialization all read this.</summary>
    public static List<ItemGenerator.Item> Loot { get; } = new();

    /// <summary>Equipped items per slot (Vision 8: equipment changes stats +
    /// tint/attached meshes). One item per EquipSlot.</summary>
    public static readonly Dictionary<ItemGenerator.EquipSlot, ItemGenerator.Item> Equipped = new();

    /// <summary>Base HP before equipment (CombatAuthority.PlayerMaxHp) — the
    /// affix vitality adds on top (recorded in BALANCE.md: affix tables).</summary>
    public const int BaseMaxHp = 100;

    public static long TotalXp => BaseXp + MatchXp;
    public static int Level => Progression.LevelForXp(TotalXp);

    /// <summary>Record the character the player took into the realm (also
    /// resets the match counters — a new session begins). `gearJson` is the
    /// character's central gear_json at pick time: the bag + equipped items
    /// are RESTORED from it (Vision 8: gear persists across ALL servers).</summary>
    public static void Select(int id, string name, string classId, int xp, string gearJson,
        int mmr = -1)
    {
        CharacterId = id;
        CharacterName = name;
        ClassId = classId;
        BaseXp = xp;
        BaseMmr = mmr;
        MmrAfter = null;
        MmrDelta = null;
        MmrTier = "";
        MatchKills = 0;
        MatchXp = 0;
        Loot.Clear();
        Equipped.Clear();
        LoadGear(gearJson);
    }

    /// <summary>Ranking mirror (Vision 8): the authority broadcast a resolved
    /// duel. Only the character THIS session picked takes the numbers — every
    /// peer receives every result, but owns at most one side of it.</summary>
    public static void OnMmr(long winnerCharacterId, long loserCharacterId, int winnerMmr,
        int loserMmr, int winnerDelta, int loserDelta, string winnerTier, string loserTier)
    {
        if (winnerCharacterId == CharacterId)
        {
            MmrAfter = winnerMmr;
            MmrDelta = winnerDelta;
            MmrTier = winnerTier;
            GD.Print($"MMR MIRROR: WON — {MmrAfter} ({MmrDelta:+0;-0}, {MmrTier})");
        }
        else if (loserCharacterId == CharacterId)
        {
            MmrAfter = loserMmr;
            MmrDelta = loserDelta;
            MmrTier = loserTier;
            GD.Print($"MMR MIRROR: LOST — {MmrAfter} ({MmrDelta:+0;-0}, {MmrTier})");
        }
    }

    /// <summary>Parse central gear_json: a JSON array of item entries. Legacy
    /// or malformed entries are skipped silently (FromJson returns null).
    /// A previously equipped item is restored to its slot.</summary>
    private static void LoadGear(string gearJson)
    {
        var items = ItemGenerator.ParseBag(gearJson);
        foreach (var item in items)
        {
            Loot.Add(item);
            // Persisted equipped state: re-equip into its slot (last write
            // wins; a malformed/duplicate-slot bag equips the newest).
            if (item.Affixes.Count > 0 || item.Slot == ItemGenerator.EquipSlot.Weapon)
                Equipped[item.Slot] = item;
        }
    }
    public static void AddLoot(ItemGenerator.Item item) => Loot.Add(item);

    /// <summary>Equip an item: it leaves the bag list view but stays in Loot
    /// (the bag IS the persistence source). Returns true when the slot took
    /// it; equipping a worse item still succeeds (swap logic is a later UX
    /// task — the inventory panel shows both totals).</summary>
    public static bool Equip(ItemGenerator.Item item)
    {
        if (!Loot.Contains(item))
            return false;
        Equipped[item.Slot] = item;
        GD.Print($"EQUIPPED: {item.Name} ({ItemGenerator.RarityLabel(item.Rarity)}) -> {item.Slot}");
        return true;
    }

    /// <summary>Loot slice 3 (Vision 8): remove the item from its slot — it
    /// stays in the bag (the bag IS the persistence source). Returns the
    /// removed item, or null when the slot was already empty.</summary>
    public static ItemGenerator.Item? Unequip(ItemGenerator.EquipSlot slot)
    {
        if (!Equipped.Remove(slot, out var removed))
            return null;
        GD.Print($"UNEQUIPPED: {removed.Name} <- {slot} (back in the bag)");
        return removed;
    }

    public static ItemGenerator.Item? EquippedItem(ItemGenerator.EquipSlot slot) =>
        Equipped.TryGetValue(slot, out var item) ? item : null;

    /// <summary>Total of one affix stat across ALL equipped items (Vision 8:
    /// affix effects on stats).</summary>
    public static int StatTotal(string stat)
    {
        int total = 0;
        foreach (var item in Equipped.Values)
            foreach (var affix in item.Affixes)
                if (affix.Stat == stat)
                    total += affix.Value;
        return total;
    }

    /// <summary>Derived: vitality affixes add max HP (BALANCE.md).
    /// Server and client read the same static helper.</summary>
    public static int DerivedMaxHp => BaseMaxHp + StatTotal("vitality");

    /// <summary>Derived: power affixes add damage percent (BALANCE.md).
    /// 0.01 per point — 10 power = +10% damage.</summary>
    public static float DerivedDamageMult => 1f + 0.01f * StatTotal("power");

    /// <summary>Derived: ward affixes add an absorb pool on spawn (BALANCE.md,
    /// stacks with the revenant kit's soul ward).</summary>
    public static int DerivedWard => StatTotal("ward");

    public static int DerivedHaste => StatTotal("haste");

    /// <summary>"+3 power, +2 vitality" summary for HUD/panels; empty when
    /// nothing is equipped.</summary>
    public static string AffixSummary()
    {
        var parts = new List<string>();
        foreach (var (stat, label) in new[] { ("power", "power"), ("haste", "haste"),
                 ("vitality", "vitality"), ("ward", "ward") })
        {
            int v = StatTotal(stat);
            if (v > 0)
                parts.Add($"+{v} {label}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>Full session bag serialized for the central gear_json column
    /// (Vision 8: drops reported by the match server, stored centrally).</summary>
    public static string SerializeGear() => ItemGenerator.BagToJson(Loot);

    public static void ResetMatch()
    {
        MatchKills = 0;
        MatchXp = 0;
        Loot.Clear();
        Equipped.Clear();
    }
}
