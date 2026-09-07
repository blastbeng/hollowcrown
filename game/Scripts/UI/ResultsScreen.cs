using System.Collections.Generic;
using Godot;
using Hollowcrown.Combat;
using Hollowcrown.Save;
using Hollowcrown.Shared;

namespace Hollowcrown.UI;

/// <summary>
/// Match results screen (vision Section 6.10 flow step: match -> results):
/// kills, XP earned, level reached with progress toward the next level, and
/// the central save outcome. Shown by Main when the player leaves the realm.
/// </summary>
public partial class ResultsScreen : CanvasLayer
{
    [Signal] public delegate void ClosedEventHandler();

    private Label _saveStatus = null!;

    public override void _Ready()
    {
        Layer = 10;   // above the arena HUD

        var dim = new ColorRect { Color = new Color(0.05f, 0.045f, 0.06f, 0.88f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(430, 0) };
        panel.AddThemeStyleboxOverride("panel", PanelBox());
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        var title = new Label
        {
            Text = "MATCH RESULTS",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", UiTheme.Accent);
        box.AddChild(title);

        string classLabel = PlayerClassInfo.Label(PlayerClassInfo.FromId(ProgressionSession.ClassId));
        AddRow(box, "CHAMPION", $"{ProgressionSession.CharacterName} — {classLabel}");
        AddSeparator(box);
        AddRow(box, "KILLS", ProgressionSession.MatchKills.ToString());
        AddRow(box, "XP EARNED", $"+{ProgressionSession.MatchXp}");

        // MMR (Vision 8): when this session's duel was RATED (both sides had
        // central characters), show rating, delta and the new tier.
        if (ProgressionSession.MmrAfter is { } mmrAfter)
        {
            int delta = ProgressionSession.MmrDelta ?? 0;
            var mmrLabel = AddRow(box, "MMR",
                $"{mmrAfter} ({delta:+0;-0}) — {ProgressionSession.MmrTier}");
            mmrLabel.AddThemeColorOverride("font_color",
                delta >= 0 ? UiTheme.Accent : UiTheme.Danger);
        }
        else
        {
            AddRow(box, "MMR", "unrated");
        }

        // Leaderboard top 5 (Vision 8: visible in the results screen).
        var lb = new Label
        {
            Text = "LEADERBOARD",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        lb.AddThemeFontSizeOverride("font_size", 18);
        lb.AddThemeColorOverride("font_color", UiTheme.Accent);
        box.AddChild(lb);
        var lbBody = new Label
        {
            Text = "loading...",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        lbBody.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(lbBody);
        _ = LoadLeaderboard(lbBody);

        // Loot gained (Vision 8 loot slice 2): full items with affixes —
        // what dropped, was picked up, and now persists in gear_json.
        if (ProgressionSession.Loot.Count > 0)
        {
            AddRow(box, "LOOT GAINED", $"{ProgressionSession.Loot.Count} item(s)");
            foreach (var item in ProgressionSession.Loot)
            {
                var lootLabel = new Label
                {
                    Text = $"· {item.Name} ({ItemGenerator.RarityLabel(item.Rarity)}) — {ItemGenerator.AffixList(item)}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                lootLabel.AddThemeFontSizeOverride("font_size", 13);
                lootLabel.AddThemeColorOverride("font_color",
                    (int)item.Rarity >= (int)ItemGenerator.Rarity.Rare
                        ? ItemGenerator.RarityColor(item.Rarity)
                        : UiTheme.Arcane);
                box.AddChild(lootLabel);
            }
        }
        else
        {
            AddRow(box, "LOOT GAINED", "none");
        }

        long total = ProgressionSession.TotalXp;
        int level = Progression.LevelForXp(total);
        AddRow(box, "LEVEL", level >= Progression.MaxLevel ? $"{level} (MAX)" : level.ToString());

        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = Progression.LevelProgress(total),
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(380, 12),
        };
        bar.AddThemeStyleboxOverride("background", SlotBox(new Color("#0d0c10"), UiTheme.PanelBorder));
        bar.AddThemeStyleboxOverride("fill", SlotBox(UiTheme.Arcane, UiTheme.Arcane));
        box.AddChild(bar);

        var xpText = new Label
        {
            Text = level >= Progression.MaxLevel
                ? $"total xp {total}"
                : $"{total - Progression.TotalXpToReach(level)}/{Progression.XpForLevel(level)} to level {level + 1}",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        xpText.AddThemeFontSizeOverride("font_size", 13);
        xpText.AddThemeColorOverride("font_color", UiTheme.ColdSteel);
        box.AddChild(xpText);

        AddSeparator(box);
        _saveStatus = new Label
        {
            Text = "saving progress...",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _saveStatus.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_saveStatus);

        var continueBtn = new Button { Text = "Continue" };
        continueBtn.Pressed += () => EmitSignal(SignalName.Closed);
        box.AddChild(continueBtn);

        GD.Print("RESULTS SCREEN READY — match stats + progression");
    }

    /// <summary>Central save outcome (Main fires the REST call async).</summary>
    public void SetSaveStatus(string text, bool ok)
    {
        _saveStatus.Text = text;
        _saveStatus.AddThemeColorOverride("font_color", ok ? UiTheme.Accent : UiTheme.Danger);
    }

    /// <summary>Leaderboard top 5 (Vision 8: visible on the results screen).
    /// Central unreachable -> quiet failure line; never blocks the screen.</summary>
    private async System.Threading.Tasks.Task LoadLeaderboard(Label body)
    {
        // Results screens are built after the menu CentralClient may be gone
        // (LeaveRealm path) — a temporary HTTP node does the query itself.
        var client = new CentralClient { Name = "LeaderboardQuery" };
        AddChild(client);
        var entries = await client.ListLeaderboard();
        client.QueueFree();
        if (entries is null || !GodotObject.IsInstanceValid(body) || !body.IsInsideTree())
            return;   // the screen was closed while the query was in flight
        if (entries.Count == 0)
        {
            body.Text = "no ranked champions yet";
            return;
        }
        var lines = new List<string>();
        for (int i = 0; i < System.Math.Min(5, entries.Count); i++)
        {
            var e = entries[i];
            lines.Add($"{i + 1}. {e.Name} — {e.Mmr} ({e.Tier})");
        }
        body.Text = string.Join("\n", lines);
    }

    /// <summary>Playtester hook: close via exec (no OS cursor click needed).
    /// Emits the same signal the Continue button does.</summary>
    public void Close() => EmitSignal(SignalName.Closed);

    /// <summary>Row builder; returns the VALUE label so callers can restyle
    /// it (MMR delta colors).</summary>
    private Label AddRow(Control box, string key, string value)
    {
        var row = new HBoxContainer();
        box.AddChild(row);
        var k = new Label { Text = key, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        k.AddThemeColorOverride("font_color", UiTheme.ColdSteel);
        k.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(k);
        var v = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Right };
        v.AddThemeFontSizeOverride("font_size", 16);
        v.AddThemeColorOverride("font_color", UiTheme.Bone);
        row.AddChild(v);
        return v;
    }

    private void AddSeparator(Control box)
    {
        var sep = new ColorRect
        {
            Color = UiTheme.PanelBorder,
            CustomMinimumSize = new Vector2(0, 1),
        };
        box.AddChild(sep);
    }

    private static StyleBoxFlat PanelBox()
    {
        var box = new StyleBoxFlat { BgColor = UiTheme.Panel };
        box.SetCornerRadiusAll(8);
        box.SetBorderWidthAll(2);
        box.SetBorderColor(UiTheme.Accent);
        box.SetContentMarginAll(22);
        return box;
    }

    private static StyleBoxFlat SlotBox(Color bg, Color border)
    {
        var box = new StyleBoxFlat { BgColor = bg };
        box.SetCornerRadiusAll(4);
        box.SetBorderWidthAll(1);
        box.SetBorderColor(border);
        box.SetContentMarginAll(4);
        return box;
    }
}
