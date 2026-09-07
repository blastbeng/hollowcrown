namespace Hollowcrown.Save;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Procedural item generation (Vision 8 loot): rarity common->mythic, a name
/// built from prefix + base + suffix tables, and affixes. Deterministic per
/// seed — the server generates the drop and the item is stored/serialized
/// centrally later. Tables are recorded in BALANCE.md. Data-driven: new
/// content = editing the tables, not code (Vision 7 rule).
/// </summary>
public static class ItemGenerator
{
    public enum Rarity { Common = 0, Uncommon, Rare, Epic, Mythic }

    public readonly record struct Affix(string Stat, int Value);

    public sealed record Item(
        string Name, Rarity Rarity, int ItemLevel, IReadOnlyList<Affix> Affixes);

    private static readonly (Rarity Rarity, double Weight)[] RarityTable =
    {
        (Rarity.Common, 50.0),
        (Rarity.Uncommon, 28.0),
        (Rarity.Rare, 15.0),
        (Rarity.Epic, 5.5),
        (Rarity.Mythic, 1.5),
    };

    // Class-neutral weapon/armor bases (Vision 8: prefix + base + suffix).
    private static readonly string[] Bases =
        { "Blade", "Dagger", "Staff", "Crown", "Gauntlets", "Pauldrons", "Sigil", "Warbelt" };

    private static readonly string[] Prefixes =
        { "Hollow", "Grim", "Ashen", "Sorrowbound", "Embermarked", "Rotted", "Vowkeeper's", "Kingsbane" };

    private static readonly string[] Suffixes =
        { "of the Abyss", "of Withering", "of the Last Vigil", "of Cinders", "of the Pale Court", "of Ruin" };

    // Affix pool: stat name + per-item-level roll range.
    private static readonly (string Stat, int MinPerLevel, int MaxPerLevel)[] AffixPool =
    {
        ("power", 1, 2),
        ("haste", 1, 1),
        ("vitality", 2, 3),
        ("ward", 1, 2),
    };

    private static readonly int[] AffixCountByRarity = { 1, 1, 2, 3, 4 };

    /// <summary>Roll one item. `rng` is seeded by the caller (deterministic
    /// server drops); itemLevel scales affix magnitudes.</summary>
    public static Item Generate(RandomNumberGenerator rng, int itemLevel)
    {
        var rarity = RollRarity(rng);
        string prefix = Prefixes[rng.RandiRange(0, Prefixes.Length - 1)];
        string baseName = Bases[rng.RandiRange(0, Bases.Length - 1)];
        bool hasSuffix = rarity >= Rarity.Rare || rng.Randf() < 0.35f;
        string name = hasSuffix
            ? $"{prefix} {baseName} {Suffixes[rng.RandiRange(0, Suffixes.Length - 1)]}"
            : $"{prefix} {baseName}";

        int affixCount = AffixCountByRarity[(int)rarity];
        var affixes = new List<Affix>(affixCount);
        for (int i = 0; i < affixCount; i++)
        {
            var pool = AffixPool[rng.RandiRange(0, AffixPool.Length - 1)];
            int value = pool.MinPerLevel * itemLevel
                        + rng.RandiRange(0, (pool.MaxPerLevel - pool.MinPerLevel) * Math.Max(1, itemLevel));
            affixes.Add(new Affix(pool.Stat, value));
        }
        return new Item(name, rarity, itemLevel, affixes);
    }

    private static Rarity RollRarity(RandomNumberGenerator rng)
    {
        double total = 0;
        foreach (var (_, weight) in RarityTable) total += weight;
        double roll = rng.Randf() * total;
        foreach (var (rarity, weight) in RarityTable)
        {
            roll -= weight;
            if (roll <= 0)
                return rarity;
        }
        return Rarity.Common;
    }

    public static string RarityLabel(Rarity r) => r switch
    {
        Rarity.Uncommon => "uncommon",
        Rarity.Rare => "rare",
        Rarity.Epic => "epic",
        Rarity.Mythic => "MYTHIC",
        _ => "common",
    };

    /// <summary>Compact JSON for the central gear_json column (Vision 8: drops
    /// reported by the match server, stored centrally).</summary>
    public static string ToJson(Item item)
    {
        var affixes = new List<string>();
        foreach (var a in item.Affixes)
            affixes.Add($"\"{a.Stat}:{a.Value}\"");
        return "{" + $"\"name\":\"{item.Name}\",\"rarity\":\"{RarityLabel(item.Rarity)}\"," +
               $"\"ilvl\":{item.ItemLevel},\"affixes\":[{string.Join(",", affixes)}]" + "}";
    }
}