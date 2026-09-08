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

    /// <summary>Equipment slot derived from the base name (Vision 8:
    /// equipment changes tint/attached meshes). Loot slice 3: weapon bases
    /// land in the Weapon slot (a loot weapon attaches to the hand socket
    /// and hides the class-default weapon).</summary>
    public enum EquipSlot { Body, Weapon }

    public sealed record Item(
        string Name, Rarity Rarity, int ItemLevel, IReadOnlyList<Affix> Affixes,
        EquipSlot Slot = EquipSlot.Body);

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

    /// <summary>Base name -> equipment slot (data-driven: editing this table
    /// adds slots, Vision 7 rule).</summary>
    private static readonly System.Collections.Generic.Dictionary<string, EquipSlot>
        BaseSlots = new()
        {
            ["Blade"] = EquipSlot.Weapon,    // loot slice 3: weapon drops equip
            ["Dagger"] = EquipSlot.Weapon,   // into the Weapon slot (mesh attach)
            ["Staff"] = EquipSlot.Weapon,
            ["Crown"] = EquipSlot.Body,
            ["Gauntlets"] = EquipSlot.Body,
            ["Pauldrons"] = EquipSlot.Body,
            ["Sigil"] = EquipSlot.Body,
            ["Warbelt"] = EquipSlot.Body,
        };

    /// <summary>Item-rarity accent colors (Vision 6.11 palette direction).
    /// LootDrop rarity glow shares these.</summary>
    public static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Uncommon => new Color("#4a8a3a"),   // moss
        Rarity.Rare => new Color("#3a6ad0"),       // deep blue
        Rarity.Epic => new Color("#8a4ad0"),       // violet
        Rarity.Mythic => new Color("#e0a03c"),     // ember gold
        _ => new Color("#9aa0a8"),                 // cold steel
    };

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
        EquipSlot slot = BaseSlots.GetValueOrDefault(baseName, EquipSlot.Body);
        return new Item(name, rarity, itemLevel, affixes, slot);
    }
    /// <summary>Parse one item back from the gear_json entry format (round
    /// trip of ToJson): {"name","rarity","ilvl","affixes"} + optional slot.
    /// Returns null on malformed input (never throws — gear_json also holds
    /// legacy entries written before this format existed).</summary>
    public static Item? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !root.TryGetProperty("name", out var nameEl))
                return null;
            string name = nameEl.GetString() ?? "";
            if (name.Length == 0)
                return null;
            // Rarity tolerates both the ToJson string form ("uncommon") and a
            // numeric index (legacy/handwritten rows — a JsonValueKind mismatch
            // on GetString() used to kill the whole load with an exception).
            Rarity rarity = Rarity.Common;
            if (root.TryGetProperty("rarity", out var rEl))
            {
                if (rEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    rarity = (rEl.GetString() ?? "") switch
                    {
                        "uncommon" => Rarity.Uncommon,
                        "rare" => Rarity.Rare,
                        "epic" => Rarity.Epic,
                        "MYTHIC" or "mythic" => Rarity.Mythic,
                        _ => Rarity.Common,
                    };
                }
                else if (rEl.ValueKind == System.Text.Json.JsonValueKind.Number &&
                         rEl.TryGetInt32(out int rIdx) &&
                         rIdx >= 0 && rIdx < (int)Rarity.Mythic)
                {
                    rarity = (Rarity)rIdx;
                }
            }
            int ilvl = root.TryGetProperty("ilvl", out var lvlEl) && lvlEl.TryGetInt32(out int l)
                ? l : 1;
            var affixes = new List<Affix>();
            if (root.TryGetProperty("affixes", out var affArr) &&
                affArr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in affArr.EnumerateArray())
                {
                    string s = el.ValueKind == System.Text.Json.JsonValueKind.String
                        ? el.GetString() ?? "" : "";
                    int colon = s.IndexOf(':');
                    if (colon > 0 && int.TryParse(s[(colon + 1)..], out int v))
                        affixes.Add(new Affix(s[..colon], v));
                }
            }
            EquipSlot slot = root.TryGetProperty("slot", out var slotEl) &&
                System.Enum.TryParse(slotEl.GetString(), out EquipSlot parsed)
                ? parsed : EquipSlot.Body;
            return new Item(name, rarity, ilvl, affixes, slot);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
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

    /// <summary>"+3 power, +2 vitality" for logs/panels.</summary>
    public static string AffixList(Item item)
    {
        var parts = new List<string>();
        foreach (var a in item.Affixes)
            parts.Add($"+{a.Value} {a.Stat}");
        return parts.Count > 0 ? string.Join(", ", parts) : "no affixes";
    }

    /// <summary>Compact JSON for the central gear_json column (Vision 8: drops
    /// reported by the match server, stored centrally).</summary>
    public static string ToJson(Item item)
    {
        var affixes = new List<string>();
        foreach (var a in item.Affixes)
            affixes.Add($"\"{a.Stat}:{a.Value}\"");
        return "{" + $"\"name\":\"{item.Name}\",\"rarity\":\"{RarityLabel(item.Rarity)}\"," +
               $"\"ilvl\":{item.ItemLevel},\"slot\":\"{item.Slot}\",\"affixes\":[{string.Join(",", affixes)}]" + "}";
    }

    /// <summary>Serialize a WHOLE bag/inventory to the gear_json column (a
    /// JSON array of ToJson entries — the central column has held '[]' since
    /// the schema was created).</summary>
    public static string BagToJson(IReadOnlyList<Item> items)
    {
        var parts = new List<string>(items.Count);
        foreach (var item in items)
            parts.Add(ToJson(item));
        return "[" + string.Join(",", parts) + "]";
    }

    /// <summary>Parse a gear_json ARRAY ("[]" / "[{...},{...}]") back into
    /// items; malformed or legacy entries are skipped. Also tolerates the
    /// JSON being a single object (legacy save shape).</summary>
    public static List<Item> ParseBag(string json)
    {
        var items = new List<Item>();
        if (string.IsNullOrWhiteSpace(json))
            return items;
        string trimmed = json.Trim();
        if (trimmed.StartsWith("["))
        {
            // Split the array on top-level objects and parse each entry with
            // the same FromJson path (one document per item).
            int depth = 0;
            int start = -1;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        var item = FromJson(trimmed[start..(i + 1)]);
                        if (item is not null)
                            items.Add(item);
                        start = -1;
                    }
                }
            }
        }
        else
        {
            var single = FromJson(trimmed);
            if (single is not null)
                items.Add(single);
        }
        return items;
    }
}