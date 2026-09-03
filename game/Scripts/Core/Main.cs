using Godot;
using Hollowcrown.Networking;
using Hollowcrown.UI;
using Hollowcrown.World;

namespace Hollowcrown.Core;

/// <summary>
/// Boot + screen flow controller. Client: login/register -> character select
/// -> server browser -> realm (arena). Dedicated server: --server skips all
/// UI, hosts the realm, and builds the SAME arena tree so CombatAuthority
/// (server-authoritative combat, Vision 2.3) validates hits against its own
/// world copy. RPC rule: nodes carrying RPCs live at identical NodePaths on
/// every peer (/root/Main/CombatAuthority), added with force_readable_name.
/// </summary>
public partial class Main : Node3D
{
    private CanvasLayer _ui = null!;
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
                AddChild(new DedicatedServer { Name = "DedicatedServer" },
                    forceReadableName: true);
                AddCombatAuthority();
                AddChild(new ArenaTest { Name = "Arena" }, forceReadableName: true);
                GD.Print("HOLLOWCROWN BOOT OK — dedicated server mode, arena hosted");
                return;
            }
        }

        _ui = new CanvasLayer { Name = "RootUI" };
        AddChild(_ui);

        _central = new CentralClient { Name = "Central" };
        AddChild(_central);

        _characters = new CharacterSelect { Name = "CharacterSelect", Visible = false };
        _characters.Bind(_central);
        _ui.AddChild(_characters);

        _browser = new ServerBrowser { Name = "ServerBrowser", Visible = false };
        _browser.Bind(_central);
        _browser.Closed += () =>
        {
            _browser.Visible = false;
            _characters.Visible = true;
        };
        _browser.RealmJoined += EnterRealm;   // connected -> hide menu, load arena
        _ui.AddChild(_browser);

        _login = new LoginScreen { Name = "Login" };
        _login.Bind(_central);
        _ui.AddChild(_login);

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

    /// <summary>Enter the realm: hide the menu UI and build the arena under
    /// the same node path the dedicated server uses (RPC paths must match).
    /// Offline (never joined) the CombatAuthority simply runs locally.</summary>
    public void EnterRealm()
    {
        AddCombatAuthority();
        if (GetNodeOrNull<Node3D>("Arena") is null)
            AddChild(new ArenaTest { Name = "Arena" }, forceReadableName: true);
        _ui.Visible = false;
        GD.Print("REALM ENTERED — arena live, combat authority attached");
    }

    private void AddCombatAuthority()
    {
        if (GetNodeOrNull("CombatAuthority") is not null)
            return;
        AddChild(new CombatAuthority { Name = "CombatAuthority" },
            forceReadableName: true);
    }
}
