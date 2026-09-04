using Godot;
using Hollowcrown.Networking;
using Hollowcrown.World;

namespace Hollowcrown.Combat;

/// <summary>
/// Ground target for combat verification (Vision 6.5: an arena with only a
/// floor is incomplete — combat needs something to hit). A gothic training
/// post: dark wood post + bone head, 1.8 m tall (Vision 6.4).
/// SERVER-AUTHORITATIVE (Vision 2.3): the dummy NEVER computes damage. It
/// registers itself with the CombatAuthority, then mirrors what the authority
/// broadcasts — damage numbers, stun ring, fall, respawn. HP shown in the
/// HUD is the authoritative value, not a local guess.
/// </summary>
public partial class TrainingDummy : StaticBody3D, ICombatTarget
{
    [Signal] public delegate void DiedEventHandler();
    [Signal] public delegate void RespawnedEventHandler();

    [Export] private int _maxHp = 100;            // BALANCE.md: dummy_hp
    public int MaxHp => _maxHp;                   // ICombatTarget (authority-owned number)
    public int Hp { get; private set; }           // mirror of the authority's value
    public bool IsDead { get; private set; }      // mirror
    public int CombatId { get; private set; }
    public Vector3 CombatPosition => GlobalPosition;
    public string DisplayName => "the Training Dummy";

    private const float FallDuration = 0.45f;

    private Node3D _visual = null!;
    private MeshInstance3D _post = null!, _head = null!, _stunRing = null!;
    private float _fallTimer, _punchTimer, _stunTimer;

    public override void _Ready()
    {
        AddToGroup("dummies");
        AddToGroup("combat_targets");   // kit candidate set (dummies + players)
        Hp = MaxHp;

        _visual = new Node3D { Name = "Visual" };
        _post = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.3f, 1.4f, 0.3f) },
            MaterialOverride = MaterialFactory.DarkWood(),
            Position = new Vector3(0, 0.7f, 0),
        };
        _head = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.28f, Height = 0.7f },
            MaterialOverride = MaterialFactory.PlayerAccent(),   // bone head reads at iso zoom
            Position = new Vector3(0, 1.5f, 0),
        };
        _visual.AddChild(_post);
        _visual.AddChild(_head);
        AddChild(_visual);

        // Stun marker (Vision 6.9): a bone ring flat on the floor while
        // stunned — readable at iso zoom, no floating icons.
        _stunRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.55f, OuterRadius = 0.75f },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.85f, 0.81f, 0.75f, 0.65f),   // bone
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0, 0.06f, 0),
            Visible = false,
        };
        AddChild(_stunRing);

        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.55f, 1.9f, 0.55f) },
            Position = new Vector3(0, 0.95f, 0),   // collider mirrors the raised visuals
        });

        CombatAuthority.For(this)?.RegisterWorldTarget(this);   // join the authority's world

        GD.Print("TRAINING DUMMY READY — 100 HP, damage flows through the match server");
    }

    public override void _ExitTree()
    {
        if (CombatId > 0)
            CombatAuthority.For(this)?.Unregister(CombatId);
    }

    public void AssignCombatId(int id) => CombatId = id;

    // ------------------- authority mirrors (all peers) ---------------------

    public void OnHitApplied(int amount, bool heavy, int hpAfter)
    {
        if (amount <= 0)
            return;
        Hp = hpAfter;
        DamageNumber.Spawn(this, GlobalPosition, amount, heavy);
        _punchTimer = 0.12f;                        // scale punch = hit feedback
    }

    /// <summary>Authority-broadcast stun (Vision 7: shield bash 0.5 s).</summary>
    public void OnStunned(float seconds)
    {
        if (IsDead || seconds <= 0f)
            return;
        _stunTimer = Mathf.Max(_stunTimer, seconds);
        GD.Print($"TRAINING DUMMY STUNNED {seconds:0.00}s (authority)");
    }

    public void OnStealthed(bool stealthed)
    {
        // Static targets can't stealth.
    }

    public void OnRooted(float seconds)
    {
        // Static targets can't be rooted.
    }

    public void OnWard(float amount)
    {
        // Static targets can't ward.
    }

    public void OnHealed(int hpAfter)
    {
        // Dummies are never healed.
    }

    public void OnKilled()
    {
        IsDead = true;
        _fallTimer = FallDuration;
        EmitSignal(SignalName.Died);
        GD.Print("TRAINING DUMMY DOWN (authority) — respawn scheduled server-side");
    }

    public void OnRespawned(int hpAfter, Vector3 spawnPos)
    {
        IsDead = false;
        Hp = hpAfter;
        _visual.RotationDegrees = Vector3.Zero;
        _post.Transparency = 0f;
        _head.Transparency = 0f;
        EmitSignal(SignalName.Respawned);
        GD.Print("TRAINING DUMMY RESPAWNED (authority) — full HP");
    }

    // ------------------------------ presentation ---------------------------

    public override void _Process(double deltaRaw)
    {
        float delta = (float)deltaRaw;

        if (_punchTimer > 0f)
        {
            _punchTimer -= delta;
            float s = 1f + 0.08f * (_punchTimer / 0.12f);
            _head.Scale = new Vector3(s, s, s);
        }
        else if (_head.Scale.X > 1f)
        {
            _head.Scale = Vector3.One;
        }

        if (IsDead)
        {
            _stunTimer = 0f;                        // stun dies with the target
            _stunRing.Visible = false;
            if (_fallTimer > 0f)                    // fall over: pitch to 90 deg
            {
                _fallTimer -= delta;
                float t = 1f - Mathf.Max(0f, _fallTimer) / FallDuration;
                _visual.RotationDegrees = new Vector3(90f * t, 0, 0);
                float a = 1f - t * 0.6f;            // fade toward 0.4 while down
                _post.Transparency = 1f - a;
                _head.Transparency = 1f - a;
            }
            // Respawn timing is server-owned; we stay down until the
            // authority broadcasts TargetRespawned.
        }
        else if (_stunTimer > 0f)
        {
            _stunTimer -= delta;
            _stunRing.Visible = true;
            _stunRing.RotationDegrees = new Vector3(0f,
                Mathf.RadToDeg(Time.GetTicksMsec() * 0.15f % 360f), 0f);   // spin = stunned
        }
        else
        {
            _stunRing.Visible = false;
        }
    }

    /// <summary>Rich state for the playtester runtime digest (mcp_watch).</summary>
    public Godot.Collections.Dictionary _mcp_state() => new()
    {
        ["hp"] = Hp,
        ["max_hp"] = MaxHp,
        ["dead"] = IsDead,
        ["combat_id"] = CombatId,
    };
}
