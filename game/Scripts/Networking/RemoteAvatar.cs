using Godot;
using Hollowcrown.Combat;
using Hollowcrown.Player;
using Hollowcrown.World;

namespace Hollowcrown.Networking;

/// <summary>
/// Mirrored body of ANOTHER peer's warden (Vision 6.8): rigged enemy model
/// (cold steel, red nameplate), lerped toward 10 Hz authority-relayed
/// positions. The local player is a full PlayerController; this node is a
/// puppet — its clips are DRIVEN: locomotion from the observed velocity,
/// hit reaction + death from authority broadcasts. It is also an
/// ICombatTarget so authority broadcasts find it by peer id on every client.
/// </summary>
public partial class RemoteAvatar : Node3D, ICombatTarget
{
    public int PeerId { get; set; }
    public string DisplayName { get; set; } = "Warden";

    public int MaxHp => CombatAuthority.PlayerMaxHp;
    public int Hp { get; private set; }
    public bool IsDead { get; private set; }
    public int CombatId => PeerId;
    public Vector3 CombatPosition => GlobalPosition;

    private const float FallDuration = 0.45f;
    private const float LerpRate = 12f;   // 1/s approach toward relayed state

    private Node3D _visual = null!;
    private WardenModel? _model;
    private Label3D _nameplate = null!;
    private Vector3 _targetPos, _lastPos;
    private float _targetYaw, _fallTimer, _punchTimer;

    public override void _Ready()
    {
        AddToGroup("combat_targets");   // kit candidate set (dummies + players)
        _targetPos = Position;
        _lastPos = Position;
        Hp = MaxHp;

        _visual = new Node3D { Name = "Visual" };
        // Rigged enemy warden (Vision 6.8): same class model, colder steel so
        // friend vs foe reads at iso zoom. The capsule stand-in is retired.
        _model = new WardenModel { Name = "Model", EnemyTint = true };
        _visual.AddChild(_model);
        AddChild(_visual);

        // Enemy nameplate: blood red, billboarded, above the head (Vision 6.8).
        _nameplate = new Label3D
        {
            Text = DisplayName,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.004f,
            FontSize = 40,
            OutlineSize = 10,
            Position = new Vector3(0, 2.35f, 0),
        };
        _nameplate.Modulate = new Color("c0392b").Lerp(new Color("7a1414"), 0.5f);
        AddChild(_nameplate);

        GD.Print($"REMOTE AVATAR READY — {DisplayName} (rigged enemy model, red nameplate)");
    }

    /// <summary>Authority-relayed state (10 Hz): the puppet lerps to it.</summary>
    public void SetNetworkTransform(Vector3 pos, float yaw)
    {
        _targetPos = pos;
        _targetYaw = yaw;
    }

    public override void _Process(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        float w = 1f - Mathf.Exp(-LerpRate * delta);

        if (IsDead)
        {
            if (_fallTimer > 0f)
            {
                _fallTimer -= delta;
                float t = 1f - Mathf.Max(0f, _fallTimer) / FallDuration;
                _model?.PlayDeath(t);
            }
            return;
        }

        GlobalPosition = GlobalPosition.Lerp(_targetPos, w);
        float yaw = Mathf.LerpAngle(Mathf.DegToRad(RotationDegrees.Y), _targetYaw, w);
        RotationDegrees = new Vector3(0, Mathf.RadToDeg(yaw), 0);

        // Locomotion clips from the OBSERVED velocity of the relay (the
        // puppet has no input of its own).
        float speed = delta > 0.001f
            ? GlobalPosition.DistanceTo(_lastPos) / delta
            : 0f;
        _lastPos = GlobalPosition;
        _model?.PlayLocomotion(speed, false);

        if (_punchTimer > 0f)                      // hit feedback (Vision 6.9)
        {
            _punchTimer -= delta;
            float s = 1f + 0.08f * (_punchTimer / 0.12f);
            _visual.Scale = new Vector3(s, s, s);
        }
        else if (_visual.Scale.X > 1f)
        {
            _visual.Scale = Vector3.One;
        }
    }

    // ------------------- authority mirrors (all peers) ---------------------

    public void AssignCombatId(int id) => PeerId = id;

    public void OnHitApplied(int amount, bool heavy, int hpAfter)
    {
        if (amount <= 0)
            return;
        Hp = hpAfter;
        DamageNumber.Spawn(this, GlobalPosition, amount, heavy);
        _punchTimer = 0.12f;
        _model?.PlayHit();
    }

    public void OnStunned(float seconds) { /* stun visual lands with PvP polish */ }

    public void OnKilled()
    {
        IsDead = true;
        _fallTimer = FallDuration;
        GD.Print($"REMOTE AVATAR DOWN — {DisplayName} (authority)");
    }

    public void OnRespawned(int hpAfter, Vector3 spawnPos)
    {
        IsDead = false;
        Hp = hpAfter;
        _model?.ResetPose();
        _visual.RotationDegrees = Vector3.Zero;
        _targetPos = spawnPos;
        GlobalPosition = spawnPos;                 // respawn teleports
        _lastPos = spawnPos;
        GD.Print($"REMOTE AVATAR RESPAWNED — {DisplayName} at {spawnPos}");
    }
}
