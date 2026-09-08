using System.Collections.Generic;
using Godot;
using Hollowcrown.Combat;
using Hollowcrown.Networking;
using Hollowcrown.Player;
using Hollowcrown.Save;
using System.Threading.Tasks;
using Hollowcrown.Shared;

namespace Hollowcrown.UI;

/// <summary>Character select / create screen (vision Section 6.10 flow step 3).</summary>
public partial class CharacterSelect : Control
{
    [Signal] public delegate void OpenServerBrowserEventHandler();

    /// <summary>Fired when a champion card is picked (Vision 6.10: select ->
    /// server browser). Main wires this to the realm flow.</summary>
    [Signal] public delegate void CharacterPickedEventHandler(string name);

    private CentralClient _central = null!;
    private VBoxContainer _cards = null!;
    private LineEdit _newName = null!;
    private OptionButton _newClass = null!;
    private Label _status = null!;

    /// <summary>Character picked on this screen (Main stores it on the central
    /// client for the progression save).</summary>
    public CharacterDto? LastPicked { get; private set; }
    public static readonly string[] ClassIds = { "warden", "nightblade", "revenant" };
    private static readonly Color[] ClassColors = { UiTheme.ColdSteel, UiTheme.Arcane, new("#4a5a3a") };

    public void Bind(CentralClient central) => _central = central;

    public override void _Ready()
    {
        Theme = UiTheme.Build();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var background = new ColorRect { Color = UiTheme.Background };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 60);
        margin.AddThemeConstantOverride("margin_right", 60);
        margin.AddThemeConstantOverride("margin_top", 30);
        margin.AddThemeConstantOverride("margin_bottom", 30);
        AddChild(margin);

        var columns = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        columns.AddThemeConstantOverride("separation", 24);
        margin.AddChild(columns);

        columns.AddChild(BuildCharacterColumn());
        columns.AddChild(BuildCreateColumn());

        _status = new Label { Text = "" };
        margin.AddChild(_status);

        VisibilityChanged += OnVisibilityChanged;
        // keyboard flow: Enter in the name field creates the character
        _newName.TextSubmitted += _text => _ = CreateCharacter();
    }

    private VBoxContainer BuildCharacterColumn()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);

        var heading = new Label { Text = "YOUR CHAMPIONS" };
        heading.AddThemeColorOverride("font_color", UiTheme.Accent);
        heading.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(heading);

        _cards = new VBoxContainer();
        _cards.AddThemeConstantOverride("separation", 8);
        box.AddChild(_cards);
        return box;
    }

    private Control BuildCreateColumn()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(340, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        var heading = new Label { Text = "CREATE" };
        heading.AddThemeColorOverride("font_color", UiTheme.Accent);
        heading.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(heading);

        _newName = new LineEdit { PlaceholderText = "character name" };
        box.AddChild(_newName);

        _newClass = new OptionButton();
        foreach (var classId in ClassIds) _newClass.AddItem(classId);
        box.AddChild(_newClass);

        var create = new Button { Text = "Create Character" };
        create.Pressed += () => _ = CreateCharacter();
        box.AddChild(create);

        var hint = new Label
        {
            Text = "warden: sword & shield\nnightblade: twin daggers\nrevenant: dark sorcery",
        };
        hint.AddThemeColorOverride("font_color", UiTheme.ColdSteel);
        box.AddChild(hint);

        var enter = new Button { Text = "Server Browser" };
        enter.Pressed += () => EmitSignal(SignalName.OpenServerBrowser);
        box.AddChild(enter);

        var leaderboard = new Button { Text = "Leaderboard" };
        leaderboard.Pressed += () => _ = ShowLeaderboard();
        box.AddChild(leaderboard);
        return panel;
    }

    private void OnVisibilityChanged()
    {
        if (Visible && _central is { IsAuthenticated: true })
        {
            _newName.GrabFocus(); // keyboard flow: login -> type name -> Enter
            _ = Refresh();
        }
    }

    private async Task Refresh()
    {
        foreach (var child in _cards.GetChildren()) child.QueueFree();
        var characters = await _central.ListCharacters();
        if (characters is null)
        {
            SetStatus("could not load characters (central unreachable?)", UiTheme.Danger);
            return;
        }
        if (characters.Count == 0)
        {
            SetStatus("no champions yet — create one on the right", UiTheme.ColdSteel);
            return;
        }
        foreach (var c in characters) _cards.AddChild(BuildCard(c));
        SetStatus($"{characters.Count} champion(s) loaded", UiTheme.ColdSteel);
    }

    private Control BuildCard(CharacterDto c)
    {
        var classIndex = System.Array.IndexOf(ClassIds, c.ClassId);
        var color = classIndex >= 0 ? ClassColors[classIndex] : UiTheme.Bone;

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(360, 0) };
        var box = new VBoxContainer();
        panel.AddChild(box);

        var name = new Label { Text = c.Name };
        name.AddThemeColorOverride("font_color", color);
        name.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(name);

        var level = Progression.LevelForXp(c.Xp);
        // MMR tiers (Vision 8): rating + tier name on every champion card.
        var detail = new Label
        {
            Text = $"{c.ClassId} — level {level} — xp {c.Xp}\n" +
                   $"mmr {c.Mmr} — {Rating.TierOf(c.Mmr)}",
        };
        detail.AddThemeColorOverride("font_color", UiTheme.Bone);
        box.AddChild(detail);

        // Card = the pick button (Vision 6.10 flow: select -> server browser).
        // Clicking arms the class for the realm + fires the pick signal; the
        // hover state uses the theme's Button stylebox.
        var pick = new Button
        {
            Text = "Select",
            CustomMinimumSize = new Vector2(0, 30),
        };
        pick.Pressed += () => Pick(c);
        box.AddChild(pick);
        return panel;
    }

    /// <summary>Pick a champion: sets PendingClass for BOTH the local body and
    /// the realm handshake (NEXT TASKS 1), stores the progression session
    /// (central XP + GEAR at pick time — loot slice 2 restores the bag) and
    /// opens the server browser.</summary>
    private void Pick(CharacterDto c)
    {
        GD.Print($"PICK CALLED: {c.Name} (id={c.Id})");
        PlayerController.PendingClass = PlayerClassInfo.FromId(c.ClassId);
        CombatAuthority.PendingClass = c.ClassId;
        // MMR (Vision 8): the handshake must carry WHO is playing (the match
        // server attributes the Elo result to the central character); the
        // session snapshots the pick-time rating as the results-screen base.
        CombatAuthority.PendingCharacterId = c.Id;
        ProgressionSession.Select(c.Id, c.Name, c.ClassId, c.Xp, c.GearJson, c.Mmr);
        LastPicked = c;
        GD.Print($"CHARACTER PICKED: {c.Name} ({c.ClassId}) id={c.Id} xp={c.Xp} " +
                 $"mmr={c.Mmr} gear={c.GearJson} — class armed, session started");
        SetStatus($"{c.Name} selected — entering the server browser", UiTheme.Accent);
        EmitSignal(SignalName.CharacterPicked, c.Name);
    }

    private async Task CreateCharacter()
    {
        var name = _newName.Text.Trim();
        if (name.Length < 2)
        {
            SetStatus("character name must be 2+ characters", UiTheme.Danger);
            return;
        }
        SetStatus("creating...", UiTheme.ColdSteel);
        var created = await _central.CreateCharacter(name, ClassIds[_newClass.Selected]);
        if (created is null)
        {
            SetStatus("create failed (name taken? central unreachable?)", UiTheme.Danger);
            return;
        }
        _newName.Text = "";
        SetStatus($"{created.Name} the {created.ClassId} enters the ranks", UiTheme.Accent);
        await Refresh();
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }

    /// <summary>Leaderboard dialog (Vision 8: visible in client): top 50 with
    /// tier names; refreshes each open. Central unreachable -> error row.</summary>
    private async System.Threading.Tasks.Task ShowLeaderboard()
    {
        var dialog = new AcceptDialog
        {
            Title = "LEADERBOARD — DUEL",
            OkButtonText = "Close",
            Exclusive = true,
        };
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, 380) };
        var body = new Label
        {
            Text = "loading...",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(400, 0),
        };
        scroll.AddChild(body);
        dialog.AddChild(scroll);
        AddChild(dialog);
        dialog.PopupCentered();
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;

        var entries = await _central.ListLeaderboard();
        if (entries is null)
        {
            body.Text = "could not load leaderboard (central unreachable?)";
            return;
        }
        if (entries.Count == 0)
        {
            body.Text = "no ranked champions yet — win rated duels";
            return;
        }
        var lines = new List<string>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            lines.Add($"{i + 1,2}. {e.Name} — {e.Mmr} — {e.Tier} — {e.ClassId} lvl {e.Level}");
        }
        body.Text = string.Join("\n", lines);
    }
}
