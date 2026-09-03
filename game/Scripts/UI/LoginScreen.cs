using Godot;
using System.Threading.Tasks;

namespace Hollowcrown.UI;

/// <summary>Login / register screen (vision Section 6.10 flow: splash -> login).</summary>
public partial class LoginScreen : Control
{
    private CentralClient _central = null!;
    private LineEdit _user = null!;
    private LineEdit _pass = null!;
    private Label _status = null!;
    private Button _login = null!;
    private Button _register = null!;

    public bool Busy { get; private set; }

    public void Bind(CentralClient central)
    {
        _central = central;
        _central.LoggedIn += _ => SetStatus($"welcome, {_central.Username}", UiTheme.Accent);
        _central.AuthFailed += message => { SetStatus(message, UiTheme.Danger); SetBusy(false); };
    }

    public override void _Ready()
    {
        Theme = UiTheme.Build();
        SetAnchorsPreset(LayoutPreset.FullRect);

        var background = new ColorRect { Color = UiTheme.Background };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        center.AddChild(panel);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        var title = new Label
        {
            Text = "HOLLOWCROWN",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 48);
        title.AddThemeColorOverride("font_color", UiTheme.Accent);
        box.AddChild(title);

        var subtitle = new Label
        {
            Text = "the crown is hollow. claim it.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        subtitle.AddThemeColorOverride("font_color", UiTheme.ColdSteel);
        box.AddChild(subtitle);

        _user = new LineEdit { PlaceholderText = "username" };
        _pass = new LineEdit { PlaceholderText = "password", Secret = true };
        box.AddChild(_user);
        box.AddChild(_pass);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        _register = new Button { Text = "Register" };
        _login = new Button { Text = "Login" };
        buttons.AddChild(_register);
        buttons.AddChild(_login);
        box.AddChild(buttons);

        _status = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(400, 0),
        };
        box.AddChild(_status);

        _register.Pressed += () => _ = Submit(readonlyLogin: false);
        _login.Pressed += () => _ = Submit(readonlyLogin: true);
        // Enter in a field walks the form; Enter on password submits (keyboard-only testing path)
        _user.TextSubmitted += _text => _pass.GrabFocus();
        _pass.TextSubmitted += _text => _ = Submit(readonlyLogin: true);

        if (_central.IsAuthenticated)
            SetStatus($"saved session: {_central.Username} — login again to refresh", UiTheme.ColdSteel);
        _user.GrabFocus();
    }

    private async Task Submit(bool readonlyLogin)
    {
        if (Busy) return;
        var user = _user.Text.Trim();
        var pass = _pass.Text;
        if (user.Length < 3 || pass.Length < 6)
        {
            SetStatus("username 3+ chars, password 6+ chars", UiTheme.Danger);
            return;
        }
        SetBusy(true);
        SetStatus(readonlyLogin ? "logging in..." : "creating account...", UiTheme.ColdSteel);
        if (readonlyLogin) await _central.Login(user, pass);
        else await _central.Register(user, pass);
    }

    public void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        _login.Disabled = busy;
        _register.Disabled = busy;
    }
}
