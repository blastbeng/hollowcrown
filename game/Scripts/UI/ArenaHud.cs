using System.Collections.Generic;
using Godot;
using Hollowcrown.Combat;
using Hollowcrown.Player;

namespace Hollowcrown.UI;

/// <summary>
/// Arena HUD (Vision 6.10 isometric convention): bottom-center ability bar
/// (Q/E/R/F slots with cooldown sweeps), stamina bar above it, target frame
/// top-center with the nearest live dummy's HP. Every element shows REAL
/// state — no placeholder bars. Player HP arrives with server-owned combat.
/// </summary>
public partial class ArenaHud : CanvasLayer
{
    [Export] public NodePath PlayerPath = null!;

    private const float TargetRange = 5.5f;

    private PlayerController _pc = null!;
    private WardenKit _kit = null!;
    private ProgressBar _staminaBar = null!;
    private Label _staminaText = null!;
    private PanelContainer _targetFrame = null!;
    private ProgressBar _targetHp = null!;
    private Label _targetText = null!;
    private readonly List<Slot> _slots = new();

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
        foreach (var child in _pc.GetChildren())
            if (child is WardenKit kit)
                _kit = kit;

        var root = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Theme = UiTheme.Build(),
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        BuildAbilityBar(root);
        BuildTargetFrame(root);
        GD.Print("ARENA HUD READY — ability bar (Q/E/R/F sweeps), stamina, target frame");
    }

    // ------------------------------ Ability bar ---------------------------

    private void BuildAbilityBar(Control root)
    {
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AnchorLeft = 0.5f; column.AnchorRight = 0.5f;
        column.AnchorTop = 1f; column.AnchorBottom = 1f;
        column.OffsetLeft = -160f; column.OffsetRight = 160f;
        column.OffsetTop = -118f; column.OffsetBottom = -14f;
        column.AddThemeConstantOverride("separation", 6);
        root.AddChild(column);

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

        bar.AddChild(MakeSlot("Q", "Chain", () => 0f, () => false));
        bar.AddChild(MakeSlot("E", "Bash",
            () => _kit is null ? 0f : _kit.BashCdRemaining / _kit.BashCooldown,
            () => false));
        bar.AddChild(MakeSlot("R", "Warcry",
            () => _kit is null ? 0f : _kit.WarcryCdRemaining / _kit.WarcryCooldown,
            () => false));
        bar.AddChild(MakeSlot("F", "Wall",
            () => 0f,
            () => _pc.IsShieldWalling));
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

    // -------------------------------- Update ------------------------------

    public override void _Process(double delta)
    {
        // Stamina (real state).
        _staminaBar.Value = _pc.Stamina;
        _staminaText.Text = $"{_pc.Stamina:0}";

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

        // Target frame: nearest alive dummy within range (real HP).
        TrainingDummy? target = null;
        float best = TargetRange;
        foreach (var node in GetTree().GetNodesInGroup("dummies"))
        {
            if (node is not TrainingDummy dummy || dummy.IsDead)
                continue;
            float d = _pc.GlobalPosition.DistanceTo(dummy.GlobalPosition);
            if (d < best)
            {
                best = d;
                target = dummy;
            }
        }
        _targetFrame.Visible = target is not null;
        if (target is not null)
        {
            _targetHp.MaxValue = target.MaxHp;
            _targetHp.Value = target.Hp;
            _targetText.Text = $"{target.Hp}/{target.MaxHp}";
        }
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
