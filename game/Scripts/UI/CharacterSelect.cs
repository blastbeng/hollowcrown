using Godot;
using System.Threading.Tasks;
using Hollowcrown.Shared;

namespace Hollowcrown.UI;

/// <summary>Character select / create screen (vision Section 6.10 flow step 3).</summary>
public partial class CharacterSelect : Control
{
    private CentralClient _central = null!;
    private VBoxContainer _cards = null!;
    private LineEdit _newName = null!;
    private OptionButton _newClass = null!;
    private Label _status = null!;
    public static readonly string[] ClassIds = { "warden", "nightblade", "revenant" };
    private static readonly Color[] ClassColors = { UiTheme.ColdSteel, UiTheme.Arcane, new("#4a5a3a") };

    public void Bind(CentralClient central) => _central = central;

    public override void _Ready()
    {
        Theme = UiTheme.Build();
        SetAnchorsPreset(LayoutPreset.FullRect);

        var background = new ColorRect { Color = UiTheme.Background };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
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

        var enter = new Button { Text = "Server Browser (next task)" };
        enter.Disabled = true;
        box.AddChild(enter);
        return panel;
    }

    private void OnVisibilityChanged()
    {
        if (Visible && _central is { IsAuthenticated: true })
            _ = Refresh();
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

        var detail = new Label
        {
            Text = $"{c.ClassId} — level {c.Level} — xp {c.Xp} — mmr {c.Mmr}",
        };
        detail.AddThemeColorOverride("font_color", UiTheme.Bone);
        box.AddChild(detail);
        return panel;
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
}
