using Godot;
using Hollowcrown.Networking;
using Hollowcrown.UI;

namespace Hollowcrown.Core;

/// <summary>
/// Boot + screen flow controller. Client: login/register -> character select ->
/// server browser. Dedicated server: --server skips all UI and hosts a realm.
/// </summary>
public partial class Main : Node3D
{
    private CentralClient _central = null!;
    private LoginScreen _login = null!;
    private CharacterSelect _characters = null!;
    private ServerBrowser _browser = null!;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--server")
            {
                AddChild(new DedicatedServer { Name = "DedicatedServer" });
                GD.Print("HOLLOWCROWN BOOT OK — dedicated server mode");
                return;
            }
        }

        var ui = new CanvasLayer { Name = "RootUI" };
        AddChild(ui);

        _central = new CentralClient { Name = "Central" };
        AddChild(_central);

        _characters = new CharacterSelect { Name = "CharacterSelect", Visible = false };
        _characters.Bind(_central);
        ui.AddChild(_characters);

        _browser = new ServerBrowser { Name = "ServerBrowser", Visible = false };
        _browser.Bind(_central);
        _browser.Closed += () =>
        {
            _browser.Visible = false;
            _characters.Visible = true;
        };
        ui.AddChild(_browser);

        _login = new LoginScreen { Name = "Login" };
        _login.Bind(_central);
        ui.AddChild(_login);

        _central.LoggedIn += _ =>
        {
            _login.Visible = false;
            _characters.Visible = true;
        };

        _characters.OpenServerBrowser += () =>
        {
            _characters.Visible = false;
            _browser.Visible = true;
        };

        GD.Print("HOLLOWCROWN BOOT OK — C# assembly loaded, main scene ready");
    }
}
