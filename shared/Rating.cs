namespace Hollowcrown.Shared;

/// <summary>
/// Elo rating + tiers (Vision 8, BALANCE.md numbers): start 1000, K=32, one
/// MMR per mode (duel for now — duel is the only scored mode implemented).
/// Implemented FULLY here so the central server and the game both compute
/// identical numbers (clients mirror only; central is authoritative).
/// </summary>
public static class Rating
{
    public const int Start = 1000;
    public const int K = 32;

    /// <summary>Elo update for ONE winner/loser pair. Expected score from the
    /// standard logistic curve: E = 1 / (1 + 10^((opponent - player)/400)).
    /// Zero-sum by construction: deltas are +K*(1-Ea) and -(that value).
    /// </summary>
    public static (int WinnerDelta, int LoserDelta) Update(int winner, int loser)
    {
        double ea = Expected(winner, loser);
        double change = K * (1.0 - ea);
        return ((int)Math.Round(change), -(int)Math.Round(change));
    }

    /// <summary>Expected score of `a` against `b` (0..1).</summary>
    public static double Expected(int a, int b) =>
        1.0 / (1.0 + Math.Pow(10.0, (b - a) / 400.0));

    /// <summary>New rating after a reported win/loss (clamped to sane floors
    /// so a farm of losses cannot go absurdly negative).</summary>
    public static int Apply(int current, int delta) =>
        Math.Max(MinRating, current + delta);

    public const int MinRating = 100;

    // ---- tiers (Vision 8: Ash -> Iron -> Bronze -> Silver -> Gold ->
    //      Obsidian -> Crown); thresholds recorded in BALANCE.md ----
    public static readonly (string Name, int Min)[] Tiers =
    {
        ("Ash", 0),
        ("Iron", 900),
        ("Bronze", 1050),
        ("Silver", 1150),
        ("Gold", 1250),
        ("Obsidian", 1400),
        ("Crown", 1600),
    };

    /// <summary>Tier name for a rating (highest tier whose threshold is met;
    /// Ash is the floor).</summary>
    public static string TierOf(int rating)
    {
        string tier = Tiers[0].Name;
        foreach (var (name, min) in Tiers)
            if (rating >= min)
                tier = name;
        return tier;
    }
}