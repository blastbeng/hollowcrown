using Godot;
using Hollowcrown.Combat;
using Hollowcrown.World;

namespace Hollowcrown.Networking;

/// <summary>
/// Mirrored body of ANOTHER peer's warden (Vision 6.8): capsule silhouette +
/// red enemy nameplate, lerped toward 10 Hz authority-relayed positions. The
/// local player is a full PlayerController; this node is a puppet. It is also
/// an ICombatTarget so authority broadcasts (damage numbers, death, respawn)
/// find it by peer id on every client.
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
    private MeshInstance3D _capsule = null!;
    private Label3D _nameplate = null!;
    private Vector3 _targetPos;
    private float _targetYaw, _fallTimer, _punchTimer;

    public override void _Ready()
    {
        _targetPos = Position;
        Hp = MaxHp;

        _visual = new Node3D { Name = "Visual" };
        _capsule = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f },
            // Cold steel for the OTHER warden — the local player wears the
            // bone accent; silhouettes stay distinguishable (Vision 6.8).
            MaterialOverride = MaterialFactory.PlayerSteel(),
            Position = new Vector3(0, 0.9f, 0),
        };
        _visual.AddChild(_capsule);
        AddChild(_visual);

        // Enemy nameplate: blood red, billboarded, above the head (Vision 6.8).
        _nameplate = new Label3D
        {
            Text = DisplayName,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.004f,
            FontSize = 40,
            OutlineSize = 10,
            Position = new Vector3(0, 2.3f, 0),
        };
        _nameplate.Modulate = new Color("c0392b").Lerp(new Color("7a1414"), 0.5f);
        AddChild(_nameplate);

        GD.Print($"REMOTE AVATAR READY — {DisplayName} (enemy, red nameplate)");
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
                _visual.RotationDegrees = new Vector3(90f * t, 0, 0);
                _capsule.Transparency = t * 0.6f;
            }
            return;
        }

        GlobalPosition = GlobalPosition.Lerp(_targetPos, w);
        float yaw = Mathf.LerpAngle(Mathf.DegToRad(RotationDegrees.Y), _targetYaw, w);
        RotationDegrees = new Vector3(0, Mathf.RadToDeg(yaw), 0);

        if (_punchTimer > 0f)                      // hit feedback (Vision 6.9)
        {
            _punchTimer -= delta;
            float s = 1f + 0.08f * (_punchTimer / 0.12f);
            _capsule.Scale = new Vector3(s, s, s);
        }
        else if (_capsule.Scale.X > 1f)
        {
            _capsule.Scale = Vector3.One;
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
        _visual.RotationDegrees = Vector3.Zero;
        _capsule.Transparency = 0f;
        _targetPos = spawnPos;
        GlobalPosition = spawnPos;                 // respawn teleports
        GD.Print($"REMOTE AVATAR RESPAWNED — {DisplayName} at {spawnPos}");
    }
}
