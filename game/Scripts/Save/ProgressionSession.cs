using Hollowcrown.Shared;

namespace Hollowcrown.Save;

/// <summary>
/// Session-scoped progression state (static: the arena builds long after the
/// menu screens are gone). The selected character flows from CharacterSelect
/// (card click); match stats are written by Main when the realm is left, and
/// are what the results screen shows and the central save reports.
/// </summary>
public static class ProgressionSession
{
    public static int CharacterId { get; private set; }
    public static string CharacterName { get; private set; } = "";
    public static string ClassId { get; private set; } = "warden";
    public static int BaseXp { get; private set; }          // central XP at pick time
    public static int MatchKills { get; set; }
    public static int MatchXp { get; set; }

    public static long TotalXp => BaseXp + MatchXp;
    public static int Level => Progression.LevelForXp(TotalXp);

    /// <summary>Record the character the player took into the realm (also
    /// resets the match counters — a new session begins).</summary>
    public static void Select(int id, string name, string classId, int xp)
    {
        CharacterId = id;
        CharacterName = name;
        ClassId = classId;
        BaseXp = xp;
        MatchKills = 0;
        MatchXp = 0;
    }

    public static void ResetMatch()
    {
        MatchKills = 0;
        MatchXp = 0;
    }
}
