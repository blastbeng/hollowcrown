using Godot;
using System.Threading.Tasks;
using Hollowcrown.Shared;

namespace Hollowcrown.UI;

/// <summary>
/// Server browser (vision Section 6.10 flow): live realms from the central
/// registry, password prompt for locked servers, direct IP join. Join attempts
/// a real ENet connection and reports the outcome; the password travels with
/// the match handshake once the dedicated server lands (next task).
/// </summary>
public partial class ServerBrowser : Control
{
    [Signal] public delegate void ClosedEventHandler();
    [Signal] public delegate void RealmJoinedEventHandler();

    private CentralClient _central = null!;
    private VBoxContainer _list = null!;
    private Label _status = null!;
    private OptionButton _modeFilter = null!;
    private LineEdit _directAddress = null!;
    private LineEdit _directPassword = null!;
    private AcceptDialog _passwordDialog = null!;
    private LineEdit _passwordField = null!;
    private ServerInfo? _pendingServer;
    private bool _busy;

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

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(box);

        // header: title + mode filter + refresh + back
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        box.AddChild(header);

        var title = new Label { Text = "SERVER BROWSER" };
        title.AddThemeColorOverride("font_color", UiTheme.Accent);
        title.AddThemeFontSizeOverride("font_size", 30);
        header.AddChild(title);

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddChild(spacer);

        _modeFilter = new OptionButton();
        foreach (var mode in new[] { "all modes", "duel", "skirmish", "open" }) _modeFilter.AddItem(mode);
        _modeFilter.ItemSelected += _selectedIndex => _ = Refresh();
        header.AddChild(_modeFilter);

        var refresh = new Button { Text = "Refresh" };
        refresh.Pressed += () => _ = Refresh();
        header.AddChild(refresh);

        var back = new Button { Text = "Back" };
        back.Pressed += () => EmitSignal(SignalName.Closed);
        header.AddChild(back);

        // live server list
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_list);

        // direct IP join (always available, vision Section 1)
        var direct = new PanelContainer();
        var directBox = new HBoxContainer();
        directBox.AddThemeConstantOverride("separation", 10);
        direct.AddChild(directBox);
        box.AddChild(direct);

        var directLabel = new Label { Text = "DIRECT JOIN" };
        directLabel.AddThemeColorOverride("font_color", UiTheme.Accent);
        directBox.AddChild(directLabel);

        _directAddress = new LineEdit
        {
            PlaceholderText = "host:port",
            CustomMinimumSize = new Vector2(220, 0),
        };
        _directAddress.TextSubmitted += _text => _ = JoinDirect();
        directBox.AddChild(_directAddress);

        _directPassword = new LineEdit
        {
            PlaceholderText = "password (optional)",
            Secret = true,
            CustomMinimumSize = new Vector2(200, 0),
        };
        _directPassword.TextSubmitted += _text => _ = JoinDirect();
        directBox.AddChild(_directPassword);

        var directJoin = new Button { Text = "Join" };
        directJoin.Pressed += () => _ = JoinDirect();
        directBox.AddChild(directJoin);

        _status = new Label { Text = "" };
        box.AddChild(_status);

        // password prompt for locked servers
        _passwordDialog = new AcceptDialog
        {
            Title = "Realm locked",
            OkButtonText = "Join",
            Exclusive = true,
        };
        var dialogBox = new VBoxContainer();
        dialogBox.AddThemeConstantOverride("separation", 8);
        _passwordDialog.AddChild(dialogBox);
        dialogBox.AddChild(new Label { Text = "This realm is password-protected." });
        _passwordField = new LineEdit { PlaceholderText = "password", Secret = true, CustomMinimumSize = new Vector2(280, 0) };
        dialogBox.AddChild(_passwordField);
        _passwordDialog.Confirmed += OnPasswordConfirmed;
        AddChild(_passwordDialog);

        VisibilityChanged += OnVisibilityChanged;
    }

    private void OnVisibilityChanged()
    {
        if (Visible && _central is { IsAuthenticated: true })
        {
            _modeFilter.GrabFocus();
            _ = Refresh();
        }
    }

    private async Task Refresh()
    {
        foreach (var child in _list.GetChildren()) child.QueueFree();
        SetStatus("querying central registry...", UiTheme.ColdSteel);

        var mode = _modeFilter.Selected <= 0 ? "" : _modeFilter.GetItemText(_modeFilter.Selected);
        var servers = await _central.ListServers(mode);
        if (servers is null)
        {
            SetStatus("could not load server list (central unreachable?)", UiTheme.Danger);
            return;
        }
        if (servers.Count == 0)
        {
            SetStatus("no realms are live — host one from the client, or run the binary with --server", UiTheme.ColdSteel);
            return;
        }

        foreach (var s in servers) _list.AddChild(BuildRow(s));
        SetStatus($"{servers.Count} realm(s) live", UiTheme.ColdSteel);
    }

    private Control BuildRow(ServerInfo s)
    {
        var panel = new PanelContainer();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        panel.AddChild(row);

        var name = new Label { Text = s.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        name.AddThemeColorOverride("font_color", UiTheme.Bone);
        name.AddThemeFontSizeOverride("font_size", 20);
        row.AddChild(name);

        var mode = new Label { Text = s.Mode };
        mode.AddThemeColorOverride("font_color", UiTheme.ColdSteel);
        row.AddChild(mode);

        var players = new Label { Text = $"{s.Players}/{s.MaxPlayers}" };
        players.AddThemeColorOverride("font_color", UiTheme.Bone);
        row.AddChild(players);

        if (s.HasPassword)
        {
            var lockBadge = new PanelContainer();
            var badgeBox = new StyleBoxFlat { BgColor = UiTheme.Danger };
            badgeBox.SetCornerRadiusAll(4);
            badgeBox.SetContentMarginAll(4);
            lockBadge.AddThemeStyleboxOverride("panel", badgeBox);
            var badge = new Label { Text = "PW" };
            badge.AddThemeColorOverride("font_color", UiTheme.Bone);
            badge.AddThemeFontSizeOverride("font_size", 13);
            lockBadge.AddChild(badge);
            row.AddChild(lockBadge);
        }

        var address = new Label { Text = $"{s.Host}:{s.Port}" };
        address.AddThemeColorOverride("font_color", UiTheme.ColdSteel);
        row.AddChild(address);

        var join = new Button { Text = "Join" };
        join.Pressed += () => RequestJoin(s);
        row.AddChild(join);
        return panel;
    }

    private void RequestJoin(ServerInfo s)
    {
        if (_busy) return;
        if (s.HasPassword)
        {
            _pendingServer = s;
            _passwordField.Text = "";
            _passwordDialog.PopupCentered();
            _passwordField.GrabFocus();
            return;
        }
        _ = Join(s.Host, s.Port, "");
    }

    private void OnPasswordConfirmed()
    {
        if (_pendingServer is null) return;
        _ = Join(_pendingServer.Host, _pendingServer.Port, _passwordField.Text);
        _pendingServer = null;
    }

    private async Task JoinDirect()
    {
        var text = _directAddress.Text.Trim();
        var sep = text.LastIndexOf(':');
        if (sep <= 0 || sep == text.Length - 1
            || !int.TryParse(text[(sep + 1)..], out var port) || port is < 1 or > 65535)
        {
            SetStatus("direct join: use host:port (e.g. 192.168.1.29:7777)", UiTheme.Danger);
            return;
        }
        await Join(text[..sep], port, _directPassword.Text);
    }

    private async Task Join(string host, int port, string password)
    {
        if (_busy) return;
        _busy = true;
        SetStatus($"connecting to {host}:{port}...", UiTheme.ColdSteel);

        var peer = new ENetMultiplayerPeer();
        if (peer.CreateClient(host, port) != Error.Ok)
        {
            _busy = false;
            SetStatus($"join failed: could not open socket to {host}:{port}", UiTheme.Danger);
            return;
        }
        Multiplayer.MultiplayerPeer = peer;

        // wait up to 3 s for the ENet handshake
        for (var i = 0; i < 30; i++)
        {
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            if (Multiplayer.MultiplayerPeer.GetConnectionStatus()
                != MultiplayerPeer.ConnectionStatus.Connecting) break;
        }

        _busy = false;
        if (Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
        {
            SetStatus($"connected to {host}:{port} — entering realm", UiTheme.Accent);
            EmitSignal(SignalName.RealmJoined);   // Main hides the menu and loads the arena
        }
        else
        {
            SetStatus($"join failed: no realm answered at {host}:{port}", UiTheme.Danger);
            Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        }
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }
}
