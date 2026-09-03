using Godot;

namespace Hollowcrown.UI;

/// <summary>
/// One code-generated Theme for ALL screens (vision Section 6.10):
/// bg #121014, accent #b08d57, danger #7a1414, bone text #d8cfc0.
/// </summary>
public static class UiTheme
{
    public static readonly Color Background = new("#121014");
    public static readonly Color Panel = new("#1b1820");
    public static readonly Color PanelBorder = new("#3a3342");
    public static readonly Color Accent = new("#b08d57");
    public static readonly Color Danger = new("#7a1414");
    public static readonly Color Bone = new("#d8cfc0");
    public static readonly Color ColdSteel = new("#8a919c");
    public static readonly Color Arcane = new("#6a4a8a");

    public static Theme Build()
    {
        var theme = new Theme();

        var panel = Box(Panel, PanelBorder);
        theme.SetStylebox("panel", "PanelContainer", panel);

        var button = Box(new Color("#241f2c"), Accent);
        var buttonHover = Box(new Color("#332b3d"), Accent);
        var buttonPressed = Box(Accent, Accent);
        theme.SetStylebox("normal", "Button", button);
        theme.SetStylebox("hover", "Button", buttonHover);
        theme.SetStylebox("pressed", "Button", buttonPressed);
        theme.SetStylebox("focus", "Button", Box(new Color("#332b3d"), Bone));
        theme.SetColor("font_color", "Button", Bone);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", new Color("#121014"));
        theme.SetFontSize("font_size", "Button", 18);

        theme.SetStylebox("normal", "LineEdit", Box(new Color("#0d0c10"), PanelBorder));
        theme.SetStylebox("focus", "LineEdit", Box(new Color("#0d0c10"), Accent));
        theme.SetColor("font_color", "LineEdit", Bone);
        theme.SetColor("caret_color", "LineEdit", Accent);
        theme.SetFontSize("font_size", "LineEdit", 18);

        theme.SetStylebox("panel", "OptionButton", button);
        theme.SetStylebox("hover", "OptionButton", buttonHover);
        theme.SetStylebox("pressed", "OptionButton", buttonPressed);
        theme.SetStylebox("focus", "OptionButton", buttonHover);
        theme.SetColor("font_color", "OptionButton", Bone);
        theme.SetFontSize("font_size", "OptionButton", 18);
        theme.SetColor("font_color", "Label", Bone);
        theme.SetFontSize("font_size", "Label", 16);

        return theme;
    }

    private static StyleBoxFlat Box(Color bg, Color border)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
        };
        box.SetCornerRadiusAll(6);
        box.SetBorderWidthAll(1);
        box.SetBorderColor(border);
        box.SetContentMarginAll(10);
        return box;
    }
}
