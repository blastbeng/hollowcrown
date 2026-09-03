using Godot;
using Hollowcrown.Shared;
using Hollowcrown.UI;

namespace Hollowcrown.Networking;

/// <summary>
/// Dedicated match server (vision Section 1/4): same binary, launched with
/// --server. Hosts an ENet realm, heartbeats the central registry every 20 s
/// (TTL 30 s) so it appears in the server browser. Flags: --port N, --name "x",
/// --password "x", --max-players N, --mode duel|skirmish|open, --central URL.
/// </summary>
public partial class DedicatedServer : Node
{
    private CentralClient _central = null!;
    private string _serverId = "";
    private string _name = "Hollowcrown Realm";
    private string _mode = "duel";
    private string _password = "";
    private string _centralUrl = CentralClient.DefaultBaseUrl;
    private int _port = 7777;
    private int _maxPlayers = 8;
    private int _players;

    public override void _Ready()
    {
        ParseArgs(OS.GetCmdlineUserArgs());
        ParseArgs(OS.GetCmdlineArgs());
        _serverId = $"{System.Environment.MachineName.ToLowerInvariant()}:{_port}";

        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(_port, _maxPlayers);
        if (err != Error.Ok)
        {
            GD.PrintErr($"SERVER_BOOT_FAILED: could not bind port {_port} (error {err})");
            GetTree().Quit(1);
            return;
        }
        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.PeerConnected += _id => _players++;
        Multiplayer.PeerDisconnected += _id => { if (_players > 0) _players--; };

        GD.Print($"SERVER_BOOT_OK name=\"{_name}\" mode={_mode} port={_port} " +
                 $"max_players={_maxPlayers} password={(_password.Length > 0 ? "yes" : "no")} " +
                 $"central={_centralUrl}");

        _central = new CentralClient { Name = "Central" };
        AddChild(_central);

        var heartbeat = new Timer { WaitTime = 20.0, Autostart = true };
        heartbeat.Timeout += () => _ = Beat();
        AddChild(heartbeat);
        _ = Beat();
    }

    private void ParseArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p):
                    _port = p;
                    i++;
                    break;
                case "--name" when i + 1 < args.Length:
                    _name = args[i + 1];
                    i++;
                    break;
                case "--password" when i + 1 < args.Length:
                    _password = args[i + 1];
                    i++;
                    break;
                case "--max-players" when i + 1 < args.Length && int.TryParse(args[i + 1], out var m):
                    _maxPlayers = m;
                    i++;
                    break;
                case "--mode" when i + 1 < args.Length:
                    _mode = args[i + 1];
                    i++;
                    break;
                case "--central" when i + 1 < args.Length:
                    _centralUrl = args[i + 1];
                    i++;
                    break;
            }
        }
    }

    private async System.Threading.Tasks.Task Beat()
    {
        var ok = await _central.Heartbeat(new ServerRegistration(
            _serverId, _name, _mode, "", _port, _players, _maxPlayers,
            _password.Length > 0));
        if (!ok) GD.PrintErr("SERVER_HEARTBEAT_FAILED: central unreachable?");
    }
}
