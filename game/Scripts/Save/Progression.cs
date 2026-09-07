namespace Hollowcrown.Save;

using System;

/// <summary>
/// XP/leveling math (Vision 8): level curve XP(N) = 100 * N^1.5 — the XP
/// needed to advance FROM level N. Kill rewards are server-owned constants
/// awarded by CombatAuthority; clients apply the SAME static curve locally
/// for HUD/results. Numbers are recorded in BALANCE.md. Raw stat gain is
/// capped later (Vision 8 horizontal progression); central caps level 100.
/// </summary>
public static class Progression
{
    public const int XpPerPlayerKill = 50;   // PvP kill reward
    public const int XpPerDummyKill = 25;    // training dummy (world targets >= 1000)
    public const int MaxLevel = 100;

    /// <summary>XP required to advance from `level` to `level + 1`.</summary>
    public static long XpForLevel(int level)
    {
        if (level < 1) level = 1;
        if (level >= MaxLevel) return long.MaxValue;   // level cap
        return (long)Math.Round(100.0 * Math.Pow(level, 1.5));
    }

    /// <summary>Total XP required to REACH `level` (cumulative curve).</summary>
    public static long TotalXpToReach(int level)
    {
        long sum = 0;
        for (int n = 1; n < level && n < MaxLevel; n++)
            sum += XpForLevel(n);
        return sum;
    }

    /// <summary>Level a total-XP value corresponds to (1..MaxLevel).</summary>
    public static int LevelForXp(long totalXp)
    {
        int level = 1;
        long remaining = Math.Max(0, totalXp);
        while (level < MaxLevel && remaining >= XpForLevel(level))
        {
            remaining -= XpForLevel(level);
            level++;
        }
        return level;
    }

    /// <summary>Progress into the current level (0..1) for HUD/results bars.</summary>
    public static float LevelProgress(long totalXp)
    {
        int level = LevelForXp(totalXp);
        if (level >= MaxLevel) return 1f;
        long floor = TotalXpToReach(level);
        long need = XpForLevel(level);
        if (need <= 0)
            return 0f;
        return Mathf01((totalXp - floor) / (double)need);
    }

    private static float Mathf01(double v) =>
        v <= 0 ? 0f : v >= 1 ? 1f : (float)v;
}
