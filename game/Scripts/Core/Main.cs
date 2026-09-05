using Godot;
using Hollowcrown.Combat;
using Hollowcrown.Networking;
using Hollowcrown.Player;
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
        // Class selection for automated/editor runs: HC_CLASS env (set by
        // remote_test.sh) overrides the warden default when no --class flag
        // was passed. The UI flow picks the class via the character card.
        if (PlayerController.PendingClass == PlayerClass.Warden)
        {
            string envClass = OS.GetEnvironment("HC_CLASS");
            if (envClass.Length > 0)
                PlayerController.PendingClass = PlayerClassInfo.FromId(envClass);
        }

        string joinHost = "";
        int joinPort = 0;
        string joinPassword = "";
        bool botMode = false;
        string botClasses = "warden";
        float quitAfter = -1f;

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
            if (arg == "--bot")
                botMode = true;
        }

        // --join host:port [--password x]: direct-IP join without the menu
        // (Vision 1: direct IP join is always available). Also the automated
        // second client for multiplayer smoke tests.
        var args = OS.GetCmdlineUserArgs();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--join" && i + 1 < args.Length)
            {
                var parts = args[++i].Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out joinPort))
                    joinHost = parts[0];
            }
            else if (args[i] == "--password" && i + 1 < args.Length)
            {
                joinPassword = args[++i];
            }
            else if (args[i] == "--class" && i + 1 < args.Length)
            {
                // Class selection for automated runs (Vision 7); the UI flow
                // picks the class via the selected character card.
                PlayerController.PendingClass = PlayerClassInfo.FromId(args[++i]);
            }
            else if (args[i] == "--bot-classes" && i + 1 < args.Length)
            {
                botClasses = args[++i].ToLowerInvariant();
            }
            else if (args[i].StartsWith("--bot-classes="))
            {
                botClasses = args[i]["--bot-classes=".Length..].ToLowerInvariant();
            }
            else if (args[i] == "--quit-after" && i + 1 < args.Length)
            {
                // Harness runtime: bot-vs-bot runs must TERMINATE (no GUI,
                // no MCP). -1 / flag omitted = run until stopped.
                float.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture,
                    out quitAfter);
            }
            else if (args[i].StartsWith("--quit-after="))
            {
                float.TryParse(args[i]["--quit-after=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture, out quitAfter);
            }
        }

        // HC_BOT (bot class list) and HC_JOIN (join target, host:port) mirror
        // --bot / --join for the playtester, which runs the game WITHOUT user
        // args (same pattern as HC_CLASS).
        string envBot = OS.GetEnvironment("HC_BOT");
        if (envBot.Length > 0)
        {
            botMode = true;
            botClasses = envBot.ToLowerInvariant();
        }
        string envJoin = OS.GetEnvironment("HC_JOIN");
        if (envJoin.Length > 0 && joinHost.Length == 0)
        {
            var envParts = envJoin.Split(':');
            if (envParts.Length == 2 && int.TryParse(envParts[1], out int envPort))
            {
                joinHost = envParts[0];
                joinPort = envPort;
            }
        }

        if (botMode)
        {
            BootBotHarness(joinHost, joinPort, joinPassword, botClasses, quitAfter);
            return;
        }

        if (joinHost.Length > 0)
        {
            // The handshake declares the class (server names the peer + every
            // peer spawns the right enemy model variant).
            CombatAuthority.PendingClass = PlayerClassInfo.Id(PlayerController.PendingClass);
            JoinRealm(joinHost, joinPort, joinPassword);
            return;
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

    /// <summary>Direct-IP realm join (Vision 1: direct join is always
    /// available): dial the realm, present the password at handshake, and
    /// load the arena immediately — the spawn lands on server approval.
    /// Also the entry point for automated multiplayer smoke tests.</summary>
    public void JoinRealm(string host, int port, string password)
    {
        // Attach the authority BEFORE dialing: ConnectedToServer (which sends
        // the password handshake) must have a subscriber when the ENet connect
        // completes — with the peer set first, the event fired into the void
        // and the server held the join unapproved forever (found live 2026-09-04).
        EnterRealm();
        CombatAuthority.PendingPassword = password;
        var peer = new ENetMultiplayerPeer();
        if (peer.CreateClient(host, port) != Error.Ok)
        {
            GD.PrintErr($"JOIN FAILED: could not open socket to {host}:{port}");
            return;
        }
        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"HOLLOWCROWN BOOT OK — joining realm at {host}:{port}");
    }

    /// <summary>Enter the realm: hide the menu UI and build the arena under
    /// the same node path the dedicated server uses (RPC paths must match).
    /// Offline (never joined) the CombatAuthority simply runs locally.</summary>
    public void EnterRealm()
    {
        AddCombatAuthority();
        if (GetNodeOrNull<Node3D>("Arena") is null)
            AddChild(new ArenaTest { Name = "Arena" }, forceReadableName: true);
        // The --join launch path calls JoinRealm from _Ready before any menu
        // UI exists — only hide the menus when they were built.
        if (_ui is not null)
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

    /// <summary>Playtester hook (same spirit as JoinRealm for smoke tests):
    /// arm one harness bot INSIDE a driven run — this is what makes the
    /// ward ABSORPTION and uncapped LEECH branches reachable offline (a
    /// real attacker while the MCP drives the player). Harness bots use
    /// explicit ids (500+) through RequestHitAs.</summary>
    public void SpawnTestBot(string classId, Vector3 pos)
    {
        if (GetNodeOrNull("TestBot") is not null)
            return;
        var bot = new CombatBot
        {
            Name = "TestBot",
            ClassId = classId,
            BotName = $"{PlayerClassInfo.Label(PlayerClassInfo.FromId(classId))}Bot",
            Position = pos,
        };
        AddChild(bot, forceReadableName: true);
        if (bot.CombatId < 0)
            bot.AssignCombatId(_nextTestBotId++);
        GD.Print($"BOT HARNESS READY — test bot armed ({classId}) at {pos}");
    }

    private int _nextTestBotId = 500;

    /// <summary>Balance harness (Vision 7 / NEXT TASKS 1): boot 1-2 bots in
    /// an arena realm, run them headless/hidden, print the winrate matrix,
    /// then quit. With --join/HC_JOIN the bots join a REALM (a bot client
    /// fights the server's realm over ENet — this is what makes warded
    /// victims + damaged casters reachable); without, the bots fight offline
    /// (fastest local matrix). Bot per-class cadence/damage rides CombatTables
    /// exactly like players (server-validated).
    /// Usage: godot --headless -- --bot [--bot-classes a+b] [--join h:p]
    /// [--quit-after 30]</summary>
    private void BootBotHarness(string joinHost, int joinPort, string joinPassword,
        string botClasses, float quitAfter)
    {
        if (joinHost.Length > 0)
        {
            // Bots joining a REALM: attach the authority + dial the host
            // FIRST (spawn approval re-registers each bot under its ENet
            // peer id — the realm is up before the bots spawn).
            EnterRealm();
            CombatAuthority.PendingPassword = joinPassword;
            var peer = new ENetMultiplayerPeer();
            if (peer.CreateClient(joinHost, joinPort) != Error.Ok)
            {
                GD.PrintErr($"BOT HARNESS: could not open socket to {joinHost}:{joinPort}");
                return;
            }
            Multiplayer.MultiplayerPeer = peer;
            GD.Print($"BOT HARNESS — joining realm at {joinHost}:{joinPort}");
        }
        else
        {
            // Offline harness: a BARE combat world (floor only). ArenaTest
            // pulls the rigged models + the UI flow — headless, its missing-
            // animation error spam (~40 MB in 3 min) starves the quit timer.
            // A harness run is judged from the authority log, not pixels.
            AddCombatAuthority();
            var floor = new StaticBody3D { Name = "HarnessFloor" };
            floor.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(60, 0.2f, 60) },
                Position = new Vector3(0, -0.1f, 0),
            });
            AddChild(floor);
            GD.Print("BOT HARNESS — offline bots on the bare combat floor");
        }

        string[] classes = botClasses.Length > 0
            ? botClasses.Split('+')
            : System.Array.Empty<string>();
        string[] valid = { "warden", "nightblade", "revenant" };
        for (int i = 0; i < classes.Length; i++)
        {
            string classId = classes[i].Trim();
            if (System.Array.IndexOf(valid, classId) < 0)
                classId = valid[i % valid.Length];
            var bot = new CombatBot
            {
                Name = $"Bot{i}",
                ClassId = classId,
                BotName = $"{PlayerClassInfo.Label(PlayerClassInfo.FromId(classId))}Bot{i}",
                Position = new Vector3(i == 0 ? -5f : 5f, 0.2f, i == 0 ? 8f : -8f),
            };
            AddChild(bot, forceReadableName: true);
            // Offline bots self-assign 500+ in _Ready; joining bots spawn at
            // id 1 until the realm approves them (their real ENet id, then).
            if (joinHost.Length == 0 && bot.CombatId < 0)
                bot.AssignCombatId(500 + i);
        }
        GD.Print($"BOT HARNESS READY — classes={botClasses} join={(joinHost.Length > 0 ? joinHost : "offline")} quit_after={quitAfter}");

        if (quitAfter > 0f)
        {
            var timer = new Timer { WaitTime = quitAfter, Autostart = true, OneShot = true };
            timer.Timeout += () =>
            {
                GD.Print("BOT HARNESS DONE — quitting (use the log above this line for the matrix)");
                GetTree().Quit();
            };
            AddChild(timer);
        }
    }
}
