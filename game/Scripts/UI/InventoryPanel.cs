using Godot;
using Hollowcrown.Networking;
using Hollowcrown.Player;
using Hollowcrown.Save;

namespace Hollowcrown.UI;

/// <summary>
/// Inventory / equipment panel (loot slice 2, Vision 8): toggled with Tab in
/// the arena. Left column = the bag (session loot), each row shows the item,
/// its rarity color, affixes and an EQUIP button; right column = equipped
/// slots + the derived stat totals (max hp / damage mult / ward). Equipping
/// broadcasts through CombatAuthority so the model retints on every peer.
/// Built in code (same pattern as the other screens) with the shared Theme.
/// </summary>
public partial class InventoryPanel : CanvasLayer
{
    private PlayerController _pc = null!;
    private VBoxContainer _bagList = null!;
    private VBoxContainer _equipList = null!;
    private Label _statSummary = null!;
    private bool _open;

    public override void _Ready()
    {
        Layer = 8;   // below results (10), above the HUD (default)
        Visible = false;

        var dim = new ColorRect { Color = new Color(0.04f, 0.035f, 0.05f, 0.82f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 120);
        margin.AddThemeConstantOverride("margin_right", 120);
        margin.AddThemeConstantOverride("margin_top", 60);
        margin.AddThemeConstantOverride("margin_bottom", 60);
        AddChild(margin);

        var columns = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        columns.AddThemeConstantOverride("separation", 28);
        margin.AddChild(columns);

        columns.AddChild(BuildBagColumn());
        columns.AddChild(BuildEquipColumn());

        // The panel binds to the LOCAL player: combat_targets also holds
        // dummies + bots, so pick the PlayerController specifically.
        _pc = FindPlayer() ?? throw new System.InvalidOperationException(
            "InventoryPanel: no PlayerController in the tree");

        if (CombatAuthority.For(this) is { } auth)
            auth.LootGranted += OnLootGranted;   // refresh the bag on pickup
        Refresh();
        GD.Print("INVENTORY PANEL READY — Tab opens the bag (Vision 8 loot slice 2)");
    }

    private static PlayerController? FindPlayer()
    {
        foreach (var node in ((SceneTree)Engine.GetMainLoop()).GetNodesInGroup("combat_targets"))
            if (node is PlayerController pc)
                return pc;
        return null;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("inventory_toggle") || e.IsActionPressed("ui_cancel"))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Toggle() => SetOpen(!_open);

    public void SetOpen(bool open)
    {
        _open = open;
        Visible = open;
        if (open)
            Refresh();
    }

    private void OnLootGranted(string itemName, string rarity)
    {
        if (_open)
            Refresh();   // new pickup shows while the panel is open
    }

    private Control BuildBagColumn()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(430, 420) };
        panel.AddThemeStyleboxOverride("panel", PanelBox());
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0) };
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        var heading = new Label { Text = "INVENTORY" };
        heading.AddThemeFontSizeOverride("font_size", 24);
        heading.AddThemeColorOverride("font_color", UiTheme.Accent);
        box.AddChild(heading);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(430, 330) };
        box.AddChild(scroll);
        _bagList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bagList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_bagList);

        var hint = new Label { Text = "walk over a glowing shard to pick it up" };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", UiTheme.ColdSteel);
        box.AddChild(hint);
        return panel;
    }

    private Control BuildEquipColumn()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(300, 420) };
        panel.AddThemeStyleboxOverride("panel", PanelBox());
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        var heading = new Label { Text = "EQUIPPED" };
        heading.AddThemeFontSizeOverride("font_size", 24);
        heading.AddThemeColorOverride("font_color", UiTheme.Accent);
        box.AddChild(heading);

        _equipList = new VBoxContainer();
        _equipList.AddThemeConstantOverride("separation", 4);
        box.AddChild(_equipList);

        var sep = new ColorRect { Color = UiTheme.PanelBorder, CustomMinimumSize = new Vector2(0, 1) };
        box.AddChild(sep);

        _statSummary = new Label { Text = "" };
        _statSummary.AddThemeFontSizeOverride("font_size", 14);
        _statSummary.AddThemeColorOverride("font_color", UiTheme.Bone);
        box.AddChild(_statSummary);

        var note = new Label
        {
            Text = "stats from equipped affixes:\nmax hp / damage mult / ward pool",
        };
        note.AddThemeFontSizeOverride("font_size", 11);
        note.AddThemeColorOverride("font_color", UiTheme.ColdSteel);
        box.AddChild(note);
        return panel;
    }

    /// <summary>Rebuild both columns from ProgressionSession (real state).</summary>
    public void Refresh()
    {
        foreach (var child in _bagList.GetChildren())
            child.QueueFree();
        foreach (var child in _equipList.GetChildren())
            child.QueueFree();

        // --- bag ---
        if (ProgressionSession.Loot.Count == 0)
        {
            var empty = new Label { Text = "bag empty — slay and loot" };
            empty.AddThemeColorOverride("font_color", UiTheme.ColdSteel);
            _bagList.AddChild(empty);
        }
        foreach (var item in ProgressionSession.Loot)
            _bagList.AddChild(BuildBagRow(item));

        // --- equipped slots ---
        foreach (ItemGenerator.EquipSlot slot in System.Enum.GetValues(typeof(ItemGenerator.EquipSlot)))
        {
            var item = ProgressionSession.EquippedItem(slot);
            _equipList.AddChild(BuildEquipRow(slot, item));
        }

        _statSummary.Text = $"max hp {ProgressionSession.DerivedMaxHp}   " +
                            $"dmg x{ProgressionSession.DerivedDamageMult:0.00}   " +
                            $"ward {ProgressionSession.DerivedWard}";
    }

    private Control BuildBagRow(ItemGenerator.Item item)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        var swatch = new ColorRect
        {
            Color = ItemGenerator.RarityColor(item.Rarity),
            CustomMinimumSize = new Vector2(6, 0),
        };
        row.AddChild(swatch);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var name = new Label
        {
            Text = $"{item.Name}  ({ItemGenerator.RarityLabel(item.Rarity)}, ilvl {item.ItemLevel})",
        };
        name.AddThemeFontSizeOverride("font_size", 14);
        name.AddThemeColorOverride("font_color",
            (int)item.Rarity >= (int)ItemGenerator.Rarity.Rare
                ? ItemGenerator.RarityColor(item.Rarity)
                : UiTheme.Bone);
        text.AddChild(name);

        var affixes = new Label { Text = ItemGenerator.AffixList(item) };
        affixes.AddThemeFontSizeOverride("font_size", 12);
        affixes.AddThemeColorOverride("font_color", UiTheme.ColdSteel);
        text.AddChild(affixes);
        row.AddChild(text);

        var equip = new Button { Text = "Equip", CustomMinimumSize = new Vector2(70, 0) };
        equip.Pressed += () =>
        {
            if (ProgressionSession.Equip(item))
            {
                CombatAuthority.For(this)?.RequestEquip();   // broadcast retint
                Refresh();
            }
        };
        row.AddChild(equip);
        return row;
    }

    private Control BuildEquipRow(ItemGenerator.EquipSlot slot, ItemGenerator.Item? item)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var label = new Label
        {
            Text = $"{slot}:  {(item is null ? "—" : item.Name)}",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color",
            item is null ? UiTheme.ColdSteel : UiTheme.Bone);
        row.AddChild(label);
        return row;
    }

    private static StyleBoxFlat PanelBox()
    {
        var box = new StyleBoxFlat { BgColor = UiTheme.Panel };
        box.SetCornerRadiusAll(8);
        box.SetBorderWidthAll(2);
        box.SetBorderColor(UiTheme.Accent);
        box.SetContentMarginAll(18);
        return box;
    }
}