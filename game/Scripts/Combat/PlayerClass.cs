namespace Hollowcrown.Combat;

/// <summary>Class identity (Vision 7): drives the model variant, the kit
/// nodes a player spawns with, and display names. Numbers per class live in
/// CombatTables + BALANCE.md.</summary>
public enum PlayerClass
{
    Warden,
    Nightblade,
    Revenant,
}

/// <summary>Id/label helpers — the id spelling matches the central server's
/// character classId ("warden" / "nightblade" / "revenant").</summary>
public static class PlayerClassInfo
{
    public static string Label(PlayerClass c) => c switch
    {
        PlayerClass.Nightblade => "Nightblade",
        PlayerClass.Revenant => "Revenant",
        _ => "Warden",
    };

    public static string Id(PlayerClass c) => Label(c).ToLowerInvariant();

    public static PlayerClass FromId(string id) => id switch
    {
        "nightblade" => PlayerClass.Nightblade,
        "revenant" => PlayerClass.Revenant,
        _ => PlayerClass.Warden,
    };
}
