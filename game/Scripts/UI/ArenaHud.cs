using System.Collections.Generic;
using Godot;
using Hollowcrown.Combat;
using Hollowcrown.Networking;
using Hollowcrown.Player;
using Hollowcrown.Save;

namespace Hollowcrown.UI;

/// <summary>
/// Arena HUD (Vision 6.10 isometric convention): bottom-center ability bar
/// (Q/E/R/F slots with cooldown sweeps), HP + stamina bars above it, target
/// frame top-center with the nearest live target's HP. Every element shows
/// REAL state — no placeholder bars; HP is the server-mirrored value.
/// </summary>
public partial class ArenaHud : CanvasLayer
{
    [Export] public NodePath PlayerPath = null!;

    private const float TargetRange = 5.5f;

    private PlayerController _pc = null!;
    private ProgressBar _hpBar = null!;
    private Label _hpText = null!;
    private ProgressBar _staminaBar = null!;
    private Label _staminaText = null!;
    private PanelContainer _targetFrame = null!;
    private ProgressBar _targetHp = null!;
    private Label _targetText = null!;
    private Label _targetName = null!;
    private PanelContainer _feedPanel = null!;
    private VBoxContainer _feed = null!;
    private Label _levelText = null!;
    private ProgressBar _xpBar = null!;
    private Button _leaveButton = null!;
    private readonly List<Slot> _slots = new();
    private readonly List<(Label Label, float Age)> _feedLines = new();

    private sealed class Slot
    {
        public Control Root = null!;
        public ColorRect Sweep = null!;
        public Label Key = null!;
        public System.Func<float> CooldownFraction = () => 0f;
        public System.Func<bool> Active = () => false;
    }

    public override void _Ready()
    {
        _pc = GetNode<PlayerController>(PlayerPath);

        var root = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Theme = UiTheme.Build(),
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // Class-agnostic ability bar (Vision 6.10): whichever kit nodes the
        // player carries (WardenChain/WardenKit, NightbladeChain/NightbladeKit)
        // provide their own slots — the HUD never hardcodes a class.
        BuildBarsAndSlots(root);
        BuildTargetFrame(root);
        BuildKillFeed(root);
        BuildLevelRow(root);
        BuildInventoryButton(root);
        if (CombatAuthority.For(this) is { } auth)
        {
            auth.KillFeed += AddKillFeed;   // server-broadcast killfeed
            auth.LootGranted += OnLootGranted;   // loot slice 2 pickup toast
        }
        GD.Print($"ARENA HUD READY — {_slots.Count} ability slots, stamina, target frame, killfeed, XP row");
    }

    /// <summary>Vision 6.10/8: top-left level + XP progress (from the live
    /// server-mirrored match counters) and the LEAVE REALM button that ends
    /// the match into the results screen.</summary>
    private void BuildLevelRow(Control root)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", SlotBox(UiTheme.Panel, UiTheme.PanelBorder));
        panel.AnchorLeft = 0f; panel.AnchorRight = 0f;
        panel.AnchorTop = 0f; panel.AnchorBottom = 0f;
        panel.OffsetLeft = 12f; panel.OffsetRight = 208f;
        panel.OffsetTop = 12f; panel.OffsetBottom = 74f;
        root.AddChild(panel);

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 3);
        panel.AddChild(box);

        _levelText = new Label
        {
            Text = "LEVEL 1",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _levelText.AddThemeFontSizeOverride("font_size", 13);
        _levelText.AddThemeColorOverride("font_color", UiTheme.Accent);
        box.AddChild(_levelText);

        _xpBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(180, 8),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _xpBar.AddThemeStyleboxOverride("background", SlotBox(new Color("#0d0c10"), UiTheme.PanelBorder));
        _xpBar.AddThemeStyleboxOverride("fill", SlotBox(UiTheme.Arcane, UiTheme.Arcane));
        box.AddChild(_xpBar);

        _leaveButton = new Button { Text = "Leave Realm" };
        _leaveButton.Pressed += () => EmitSignal(SignalName.LeaveRealm);
        box.AddChild(_leaveButton);
    }

    /// <summary>Fired by the Leave Realm button — Main shows the results
    /// screen and saves progression to central (Vision 6.10 flow).</summary>
    [Signal] public delegate void LeaveRealmEventHandler();

    // ---------------------- Inventory (loot slice 2) -----------------------

    private CanvasLayer? _inventory;
    private Label? _lootToast;
    private float _lootToastAge;

    private void BuildInventoryButton(Control root)
    {
        var btn = new Button { Text = "Inventory (Tab)" };
        btn.AnchorLeft = 0f; btn.AnchorRight = 0f;
        btn.AnchorTop = 0f; btn.AnchorBottom = 0f;
        btn.OffsetLeft = 12f; btn.OffsetRight = 208f;
        btn.OffsetTop = 78f; btn.OffsetBottom = 104f;
        btn.Pressed += ToggleInventory;
        root.AddChild(btn);
    }

    /// <summary>Open/close the inventory panel (button or Tab action).</summary>
    public void ToggleInventory()
    {
        _inventory ??= new UI.InventoryPanel { Name = "InventoryPanel" };
        AddChild(_inventory);
        if (_inventory is UI.InventoryPanel panel)
            panel.Toggle();
    }

    private void OnLootGranted(string itemName, string rarity)
    {
        _lootToast ??= MakeLootToast();
        _lootToast.Text = $"+ {itemName} ({rarity})";
        _lootToastAge = 0f;
        _lootToast.Visible = true;
    }

    private Label MakeLootToast()
    {
        var label = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        label.OffsetTop = 90f; label.OffsetBottom = 118f;
        label.OffsetLeft = -220f; label.OffsetRight = 220f;
        label.AddThemeFontSizeOverride("font_size", 16);
        AddChild(label);
        return label;
    }

    // ------------------------------ Ability bar ---------------------------

    private void BuildBarsAndSlots(Control root)
    {
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AnchorLeft = 0.5f; column.AnchorRight = 0.5f;
        column.AnchorTop = 1f; column.AnchorBottom = 1f;
        column.OffsetLeft = -160f; column.OffsetRight = 160f;
        column.OffsetTop = -118f; column.OffsetBottom = -14f;
        column.AddThemeConstantOverride("separation", 6);
        root.AddChild(column);

        // HP row: server-mirrored combat state (Vision 2.3 — never a local guess).
        var hpRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        hpRow.AddThemeConstantOverride("separation", 8);
        column.AddChild(hpRow);

        _hpBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(280, 14),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _hpBar.AddThemeStyleboxOverride("background",
            SlotBox(new Color("#0d0c10"), UiTheme.PanelBorder));
        _hpBar.AddThemeStyleboxOverride("fill",
            SlotBox(UiTheme.Danger, UiTheme.Danger));
        hpRow.AddChild(_hpBar);

        _hpText = new Label
        {
            Text = "100/100",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _hpText.AddThemeFontSizeOverride("font_size", 12);
        hpRow.AddChild(_hpText);

        // Stamina row: bar + numeric readout (real PlayerController state).
        var staminaRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        staminaRow.AddThemeConstantOverride("separation", 8);
        column.AddChild(staminaRow);

        _staminaBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(280, 14),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _staminaBar.AddThemeStyleboxOverride("background",
            SlotBox(new Color("#0d0c10"), UiTheme.PanelBorder));
        _staminaBar.AddThemeStyleboxOverride("fill",
            SlotBox(UiTheme.Accent, UiTheme.Accent));
        staminaRow.AddChild(_staminaBar);

        _staminaText = new Label
        {
            Text = "100",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _staminaText.AddThemeFontSizeOverride("font_size", 12);
        staminaRow.AddChild(_staminaText);

        var bar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.Alignment = BoxContainer.AlignmentMode.Center;
        bar.AddThemeConstantOverride("separation", 8);
        column.AddChild(bar);

        foreach (var child in _pc.GetChildren())
        {
            if (child is not IAbilityProvider provider)
                continue;
            foreach (var slot in provider.Slots())
                bar.AddChild(MakeSlot(slot.Key, slot.Name, slot.CdFraction, slot.Active));
        }
    }

    private Control MakeSlot(string keyLabel, string abilityName,
        System.Func<float> cooldownFraction, System.Func<bool> active)
    {
        var slot = new Slot();
        var root = new Control
        {
            CustomMinimumSize = new Vector2(64, 64),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        slot.Root = root;

        var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", SlotBox(UiTheme.Panel, UiTheme.PanelBorder));
        root.AddChild(panel);

        var sweep = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.72f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        sweep.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        root.AddChild(sweep);
        slot.Sweep = sweep;

        var key = new Label { Text = keyLabel, MouseFilter = Control.MouseFilterEnum.Ignore };
        key.Position = new Vector2(6, 3);
        key.AddThemeFontSizeOverride("font_size", 15);
        key.AddThemeColorOverride("font_color", UiTheme.Accent);
        root.AddChild(key);
        slot.Key = key;

        var name = new Label
        {
            Text = abilityName,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        name.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        name.OffsetTop = -22f; name.OffsetBottom = -5f; name.OffsetLeft = 4f; name.OffsetRight = -4f;
        name.AddThemeFontSizeOverride("font_size", 12);
        root.AddChild(name);

        slot.CooldownFraction = cooldownFraction;
        slot.Active = active;
        _slots.Add(slot);
        return root;
    }

    // ------------------------------ Target frame --------------------------

    private void BuildTargetFrame(Control root)
    {
        _targetFrame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _targetFrame.AddThemeStyleboxOverride("panel", SlotBox(UiTheme.Panel, UiTheme.PanelBorder));
        _targetFrame.AnchorLeft = 0.5f; _targetFrame.AnchorRight = 0.5f;
        _targetFrame.AnchorTop = 0f; _targetFrame.AnchorBottom = 0f;
        _targetFrame.OffsetLeft = -110f; _targetFrame.OffsetRight = 110f;
        _targetFrame.OffsetTop = 14f; _targetFrame.OffsetBottom = 66f;
        root.AddChild(_targetFrame);

        var vbox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        vbox.AddThemeConstantOverride("separation", 2);
        _targetFrame.AddChild(vbox);

        var nameLabel = new Label
        {
            Text = "TRAINING DUMMY",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(nameLabel);
        _targetName = nameLabel;

        _targetHp = new ProgressBar
        {
            MaxValue = 100, Value = 100, ShowPercentage = false,
            CustomMinimumSize = new Vector2(200, 10),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _targetHp.AddThemeStyleboxOverride("background",
            SlotBox(new Color("#0d0c10"), UiTheme.PanelBorder));
        _targetHp.AddThemeStyleboxOverride("fill", SlotBox(UiTheme.Danger, UiTheme.Danger));
        vbox.AddChild(_targetHp);

        _targetText = new Label
        {
            Text = "100/100",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _targetText.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(_targetText);
    }

    // ------------------------------- Killfeed ------------------------------

    private void BuildKillFeed(Control root)
    {
        _feedPanel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _feedPanel.AddThemeStyleboxOverride("panel",
            SlotBox(new Color(0.05f, 0.045f, 0.06f, 0.6f), new Color(0, 0, 0, 0)));
        _feedPanel.AnchorLeft = 1f; _feedPanel.AnchorRight = 1f;
        _feedPanel.AnchorTop = 0f; _feedPanel.AnchorBottom = 0f;
        _feedPanel.OffsetLeft = -280f; _feedPanel.OffsetRight = -12f;
        _feedPanel.OffsetTop = 12f; _feedPanel.OffsetBottom = 150f;
        root.AddChild(_feedPanel);

        _feed = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _feed.Alignment = BoxContainer.AlignmentMode.End;   // newest lines sit low, feed grows upward
        _feedPanel.AddChild(_feed);
    }

    /// <summary>Killfeed line — fed by the CombatAuthority broadcast (Vision
    /// 6.10). Newest at the bottom; lines fade after 4 s.</summary>
    public void AddKillFeed(string text)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", Color.FromHtml("d8cfc0"));   // bone
        _feed.AddChild(label);
        _feedLines.Add((label, 0f));
        while (_feedLines.Count > 5)
        {
            _feedLines[0].Label.QueueFree();
            _feedLines.RemoveAt(0);
        }
        _feedPanel.Visible = true;
    }

    // -------------------------------- Update ------------------------------

    public override void _Process(double delta)
    {
        // Loot toast fade (loot slice 2): visible 2.5 s, gone by 3.5 s.
        if (_lootToast is { Visible: true })
        {
            _lootToastAge += (float)delta;
            _lootToast.Modulate = new Color(1, 1, 1,
                _lootToastAge > 2.5f ? Mathf.Clamp(1f - (_lootToastAge - 2.5f), 0f, 1f) : 1f);
            if (_lootToastAge > 3.5f)
                _lootToast.Visible = false;
        }

        // HP + stamina (real state; HP mirrors the match server).
        _hpBar.MaxValue = _pc.MaxHp;
        _hpBar.Value = _pc.Hp;
        _hpText.Text = $"{_pc.Hp}/{_pc.MaxHp}";
        _staminaBar.Value = _pc.Stamina;
        _staminaText.Text = $"{_pc.Stamina:0}";

        // Level + XP row (Vision 8): live from the authority-mirrored match
        // counters on top of the character's central XP.
        long totalXp = ProgressionSession.TotalXp;
        int level = Progression.LevelForXp(totalXp);
        _levelText.Text = level >= Progression.MaxLevel
            ? $"LEVEL {level} (MAX) — {_pc.MatchKills} kills"
            : $"LEVEL {level} — {_pc.MatchKills} kills";
        _xpBar.Value = Progression.LevelProgress(totalXp);

        // Cooldown sweeps + active glow.
        foreach (var slot in _slots)
        {
            float frac = Mathf.Clamp(slot.CooldownFraction(), 0f, 1f);
            slot.Sweep.Visible = frac > 0.001f;
            if (slot.Sweep.Visible)
                slot.Sweep.OffsetBottom = frac * slot.Root.Size.Y;
            bool active = slot.Active();
            slot.Key.AddThemeColorOverride("font_color",
                active ? Colors.White : UiTheme.Accent);
        }

        // Target frame: nearest alive combat target in range — dummy OR enemy
        // warden — showing the server-mirrored HP (Vision 6.10).
        ICombatTarget? target = null;
        float best = TargetRange;
        foreach (var node in GetTree().GetNodesInGroup("combat_targets"))
        {
            if (node is not Node3D n3 || node is not ICombatTarget candidate)
                continue;
            if (candidate.IsDead || n3 == _pc)
                continue;
            float d = _pc.GlobalPosition.DistanceTo(n3.GlobalPosition);
            if (d < best)
            {
                best = d;
                target = candidate;
            }
        }
        _targetFrame.Visible = target is not null;
        if (target is not null)
        {
            _targetName.Text = target.DisplayName.ToUpperInvariant();
            _targetHp.MaxValue = target.MaxHp;
            _targetHp.Value = target.Hp;
            _targetText.Text = $"{target.Hp}/{target.MaxHp}";
        }

        // Killfeed fade (age > 4 s fades out, gone by 5.5 s).
        for (int i = _feedLines.Count - 1; i >= 0; i--)
        {
            var (label, age) = _feedLines[i];
            age += (float)delta;
            label.Modulate = new Color(1, 1, 1,
                age > 4f ? Mathf.Clamp(1f - (age - 4f) / 1.5f, 0f, 1f) : 1f);
            if (age > 5.5f)
            {
                label.QueueFree();
                _feedLines.RemoveAt(i);
            }
            else
            {
                _feedLines[i] = (label, age);
            }
        }
        _feedPanel.Visible = _feedLines.Count > 0;
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
