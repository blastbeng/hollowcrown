using Godot;
using Hollowcrown.Core;
using Hollowcrown.Networking;
using Hollowcrown.Player;

namespace Hollowcrown.Combat;

/// <summary>
/// Warden 3-hit sword arc chain (Vision 7): Q / LMB, each press advances the
/// combo while the window is open; the finisher is heavier. The hitbox is a
/// GROUND-PROJECTED arc sector (radius x 120 deg) anchored on the player and
/// aimed at the cursor ground point — the flash mesh IS the hitbox, drawn flat
/// on the floor (Vision 6.9). SERVER-AUTHORITATIVE (Vision 2.3): this client
/// only draws the swing and REQUESTS hits by victim id; CombatAuthority
/// validates range/arc/cooldown against its own world and computes every
/// damage number (CombatTables -> BALANCE.md).
/// </summary>
public partial class WardenChain : Node3D
{
    [Signal] public delegate void ChainSwingEventHandler(int index, Vector3 aimPoint);

    [Export] public float Reach = 2.4f;           // BALANCE.md: warden_chain
    [Export] public float ArcDegrees = 120f;
    [Export] public float ComboWindow = 0.9f;

    private const int SwingCount = 3;             // swings in the chain

    private CharacterBody3D _body = null!;
    private Camera3D? _cam;
    private MeshInstance3D? _arcFlash;
    private int _combo;
    private float _comboTimer, _flashTimer;

    public override void _Ready()
    {
        _body = (CharacterBody3D)GetParent();
        GD.Print("WARDEN CHAIN READY — Q/LMB: 3-hit sword arc, 120deg x 2.4m ground-projected hitbox (damage server-computed)");
    }

    public override void _Process(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        _cam ??= GetViewport().GetCamera3D();
        if (_cam is null)
            return;

        if (_comboTimer > 0f)
        {
            _comboTimer -= delta;
            if (_comboTimer <= 0f)
                _combo = 0;                        // window closed: chain resets
        }

        if (Input.IsActionJustPressed("attack"))
            Swing();

        if (_flashTimer > 0f)
        {
            _flashTimer -= delta;
            if (_flashTimer <= 0f && _arcFlash is not null)
                _arcFlash.Visible = false;
        }
    }

    private void Swing()
    {
        // No swinging while down or authority-stunned (Vision 7 control rules).
        if (_body is PlayerController pc && (pc.IsDead || pc.IsStunned))
            return;

        Vector3 aim = Aim.CursorGroundPoint(_cam!);
        var toAim = aim - _body.GlobalPosition;
        toAim.Y = 0f;
        if (toAim.LengthSquared() < 0.0001f)
            toAim = -_body.GlobalBasis.Z;          // aim on self: swing forward
        Vector3 facing = toAim.Normalized();

        // Face the swing so the silhouette reads the attack (Vision 6.8).
        _body.RotationDegrees = new Vector3(0f,
            Mathf.RadToDeg(Mathf.Atan2(-facing.X, -facing.Z)), 0f);

        int index = _combo;
        _combo = (_combo + 1) % SwingCount;
        _comboTimer = ComboWindow;

        ShowArc(facing);
        var authority = CombatAuthority.For(this);
        int attackId = index switch
        {
            0 => (int)AttackId.ChainLight,
            1 => (int)AttackId.ChainMid,
            _ => (int)AttackId.ChainFinisher,
        };
        // Local arc filter is PREDICTION only — the server re-validates every
        // request against its own world before applying any damage. Candidates
        // are ALL combat targets: dummies and OTHER wardens (PvP, Vision 9).
        int requests = 0;
        foreach (var node in GetTree().GetNodesInGroup("combat_targets"))
        {
            if (node is not Node3D target || target == _body)
                continue;
            var to = target.GlobalPosition - _body.GlobalPosition;
            to.Y = 0f;
            if (to.Length() > Reach + 0.35f)       // + victim half-width
                continue;
            if (Mathf.RadToDeg(facing.AngleTo(to.Normalized())) > ArcDegrees * 0.5f)
                continue;
            if (node is ICombatTarget combatTarget)
                authority?.RequestHit(combatTarget.CombatId, attackId, _body.GlobalPosition, facing);
            requests++;
        }
        EmitSignal(SignalName.ChainSwing, index, aim);
        GD.Print($"WARDEN SWING {index + 1}/3 attack={attackId} requests={requests} (server computes damage)");
    }

    /// <summary>Ground-projected arc sector flash — the visible hitbox.</summary>
    private void ShowArc(Vector3 facing)
    {
        if (_arcFlash is null)
        {
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.69f, 0.55f, 0.34f, 0.35f),  // accent, 35%
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            _arcFlash = new MeshInstance3D
            {
                Name = "ArcFlash",
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_arcFlash);
        }

        _arcFlash.Mesh = GroundShapes.Sector(Reach, ArcDegrees, facing);
        _arcFlash.GlobalPosition = new Vector3(_body.GlobalPosition.X, 0.03f, _body.GlobalPosition.Z);
        _arcFlash.Visible = true;
        _flashTimer = 0.15f;
    }
}
