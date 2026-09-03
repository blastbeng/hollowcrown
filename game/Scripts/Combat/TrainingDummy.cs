using Godot;
using Hollowcrown.World;

namespace Hollowcrown.Combat;

/// <summary>
/// Ground target for combat verification (Vision 6.5: an arena with only a
/// floor is incomplete — combat needs something to hit). A gothic training
/// post: dark wood post + bone head, 1.8 m tall (Vision 6.4). Damage is
/// APPLIED here but never COMPUTED here — attackers call TakeDamage; the
/// match server will own the numbers once combat goes online (Vision 2.3).
/// </summary>
public partial class TrainingDummy : StaticBody3D
{
    [Signal] public delegate void DiedEventHandler();
    [Signal] public delegate void RespawnedEventHandler();

    [Export] public int MaxHp = 100;              // BALANCE.md: dummy_hp
    public int Hp { get; private set; }
    public bool IsDead { get; private set; }

    private const float FallDuration = 0.45f;
    private const float RespawnDelay = 3.0f;

    private Node3D _visual = null!;
    private MeshInstance3D _post = null!, _head = null!;
    private float _fallTimer, _respawnTimer, _punchTimer;

    public override void _Ready()
    {
        AddToGroup("dummies");
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

        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.55f, 1.9f, 0.55f) },
            Position = new Vector3(0, 0.95f, 0),   // collider mirrors the raised visuals
        });

        GD.Print("TRAINING DUMMY READY — 100 HP, group 'dummies'");
    }

    /// <summary>Apply damage. Returns true if the hit landed (target alive).</summary>
    public bool TakeDamage(int amount, bool heavy)
    {
        if (IsDead || amount <= 0)
            return false;
        Hp -= amount;
        DamageNumber.Spawn(this, GlobalPosition, amount, heavy);
        _punchTimer = 0.12f;                        // scale punch = hit feedback
        if (Hp <= 0)
        {
            Hp = 0;
            IsDead = true;
            _fallTimer = FallDuration;
            _respawnTimer = RespawnDelay;
            EmitSignal(SignalName.Died);
            GD.Print($"TRAINING DUMMY DOWN — respawning in {RespawnDelay:0.0}s");
        }
        return true;
    }

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
            if (_fallTimer > 0f)                    // fall over: pitch to 90 deg
            {
                _fallTimer -= delta;
                float t = 1f - Mathf.Max(0f, _fallTimer) / FallDuration;
                _visual.RotationDegrees = new Vector3(90f * t, 0, 0);
                float a = 1f - t * 0.6f;            // fade toward 0.4 while down
                _post.Transparency = 1f - a;
                _head.Transparency = 1f - a;
            }
            else
            {
                _respawnTimer -= delta;
                if (_respawnTimer <= 0f)
                    Respawn();
            }
        }
    }

    private void Respawn()
    {
        IsDead = false;
        Hp = MaxHp;
        _visual.RotationDegrees = Vector3.Zero;
        _post.Transparency = 0f;
        _head.Transparency = 0f;
        EmitSignal(SignalName.Respawned);
        GD.Print("TRAINING DUMMY RESPAWNED — 100 HP");
    }

    /// <summary>Rich state for the playtester runtime digest (mcp_watch).</summary>
    public Godot.Collections.Dictionary _mcp_state() => new()
    {
        ["hp"] = Hp,
        ["max_hp"] = MaxHp,
        ["dead"] = IsDead,
        ["respawn_in"] = Mathf.Max(0f, _respawnTimer),
    };
}
