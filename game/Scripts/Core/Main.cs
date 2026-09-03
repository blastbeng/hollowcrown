using Godot;

namespace Hollowcrown.Core;

/// <summary>
/// Boot scene root. Proves the C# assembly loads, the engine boots, and the
/// Section 6.10 palette drives the first UI. All later screens extend this theme.
/// </summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        var ui = new CanvasLayer { Name = "BootUI" };

        var background = new ColorRect
        {
            Color = new Color("#121014"), // UI bg
        };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        background.AddChild(center);

        var box = new VBoxContainer();
        center.AddChild(box);

        var title = new Label
        {
            Text = "HOLLOWCROWN",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 72);
        title.AddThemeColorOverride("font_color", new Color("#b08d57")); // UI accent
        box.AddChild(title);

        var subtitle = new Label
        {
            Text = "dark fantasy isometric PvP MMO — boot OK",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        subtitle.AddThemeFontSizeOverride("font_size", 22);
        subtitle.AddThemeColorOverride("font_color", new Color("#d8cfc0")); // bone
        box.AddChild(subtitle);

        AddChild(ui);

        GD.Print("HOLLOWCROWN BOOT OK — C# assembly loaded, main scene ready");
    }
}
