using Godot;
using Hollowcrown.World;

namespace Hollowcrown.Player;

/// <summary>
/// Player controller (Vision 1 / 6.8 / 7): WASD movement RELATIVE TO THE ISO
/// CAMERA YAW (rig never free-rotates), sprint on stamina, dodge roll on
/// Space with a 0.3 s i-frame window (Vision 7), procedural silhouette
/// animation (bob / lean / roll) with hook points a real AnimationPlayer can
/// replace later (Vision 6.8: "SOMETHING must move").
/// Tunables are recorded in BALANCE.md; server-authoritative combat lands
/// with the combat core task and will consume IsInvulnerable.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    // --- Tunables (BALANCE.md: movement + stamina) ---
    [Export] public float WalkSpeed = 4.5f;
    [Export] public float SprintSpeed = 7.0f;
    [Export] public float Accel = 18f;              // velocity approach rate (1/s)
    [Export] public float DodgeSpeed = 9.5f;
    [Export] public float DodgeDuration = 0.3f;     // == i-frame window (Vision 7)
    [Export] public float DodgeCooldown = 0.9f;
    [Export] public float DodgeCost = 25f;
    [Export] public float StaminaMax = 100f;
    [Export] public float SprintDrain = 20f;        // per second
    [Export] public float StaminaRegen = 15f;       // per second
    [Export] public float StaminaRegenDelay = 1.0f; // after last sprint/dodge
    [Export] public float TurnRate = 14f;           // yaw approach rate (1/s)
    [Export] public float Gravity = 18f;

    // --- Live state (HUD + combat core read these) ---
    public float Stamina { get; private set; }
    public bool IsDodging { get; private set; }
    public bool IsInvulnerable => IsDodging;        // Vision 7: i-frames
    public bool IsSprinting { get; private set; }

    // --- Animation hooks (Vision 6.8): swap for a real AnimationPlayer. ---
    [Signal] public delegate void FootstepEventHandler();
    [Signal] public delegate void DodgeStartedEventHandler();

    private Node3D _visualRoot = null!;   // pivot for bob/lean/roll
    private MeshInstance3D _capsule = null!;
    private float _dodgeTimer, _dodgeCdTimer, _staminaIdleTimer;
    private float _bobPhase, _bobLastSin;
    private Vector3 _lastMoveDir = Vector3.Forward;
    private Vector3 _dodgeDir;
    private Vector3 _rollAxisLocal;   // tumble axis in body space, frozen at dodge start
    private static readonly Vector3 CapsuleCenter = new(0f, 0.9f, 0f);  // 1.8 m capsule mid-height
    private Camera3D? _cam;

    public override void _Ready()
    {
        // 1.8 m capsule stand-in (Vision 6.4) with the steel tint; the real
        // rigged class model replaces this child in the character task.
        _visualRoot = new Node3D { Name = "VisualRoot" };
        _capsule = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f },
            MaterialOverride = MaterialFactory.PlayerAccent(),  // bone accent: silhouette reads vs floor (Vision 6.8)
            Position = new Vector3(0f, 0.9f, 0f),
        };
        _visualRoot.AddChild(_capsule);
        AddChild(_visualRoot);

        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f },
            Position = new Vector3(0f, 0.9f, 0f),
        });

        FloorSnapLength = 0.4f;
        Stamina = StaminaMax;
        GD.Print("PLAYER CONTROLLER READY — WASD camera-relative, sprint, dodge roll (0.3 s i-frames)");
    }

    public override void _PhysicsProcess(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        _cam = GetViewport().GetCamera3D() ?? _cam;

        UpdateStamina(delta);
        UpdateDodgeTimers(delta);
        HandleDodgeInput();

        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var moveDir = CameraRelative(input);

        if (IsDodging)
        {
            Velocity = _dodgeDir * DodgeSpeed;    // burst, no steering
            AnimateDodgeSpin(delta);
        }
        else
        {
            if (moveDir.LengthSquared() > 0.01f)
                _lastMoveDir = moveDir;

            bool wantsSprint = Input.IsActionPressed("sprint")
                               && moveDir.LengthSquared() > 0.01f;
            IsSprinting = wantsSprint && Stamina > 1f;

            var goal = moveDir * (IsSprinting ? SprintSpeed : WalkSpeed);
            Velocity = Velocity.Lerp(goal, 1f - Mathf.Exp(-Accel * delta));
            if (!IsOnFloor())
                Velocity += Vector3.Down * Gravity * delta;

            FaceMovement(delta);
            AnimateLocomotion(delta);
        }

        MoveAndSlide();
    }

    /// <summary>WASD intent re-based on the camera yaw (Vision 1: movement is
    /// camera-relative; the rig yaw is locked at 45 deg).</summary>
    private Vector3 CameraRelative(Vector2 input)
    {
        if (_cam is null || input.LengthSquared() < 0.001f)
            return Vector3.Zero;
        var back = _cam.GlobalBasis.Z;        // camera's backward axis
        back = new Vector3(back.X, 0f, back.Z).Normalized();
        var right = _cam.GlobalBasis.X;
        right = new Vector3(right.X, 0f, right.Z).Normalized();
        return (right * input.X + back * input.Y).Normalized();
        // move_forward contributes y = -1 => -back = camera-forward. Correct:
        // W walks AWAY from the camera (into the screen), like Diablo/V Rising.
    }

    private void HandleDodgeInput()
    {
        if (!Input.IsActionJustPressed("dodge_roll") || IsDodging)
            return;
        if (_dodgeCdTimer > 0f || Stamina < DodgeCost)
            return;

        Stamina -= DodgeCost;
        _staminaIdleTimer = StaminaRegenDelay;
        _dodgeDir = _lastMoveDir;
        var axisWorld = Vector3.Up.Cross(_dodgeDir);
        if (axisWorld.LengthSquared() < 0.001f)
            axisWorld = Vector3.Right;
        _rollAxisLocal = GlobalBasis.Inverse() * axisWorld.Normalized();
        _dodgeTimer = DodgeDuration;
        _dodgeCdTimer = DodgeCooldown;
        IsDodging = true;
        EmitSignal(SignalName.DodgeStarted);   // animation hook
    }

    private void UpdateDodgeTimers(float delta)
    {
        if (_dodgeCdTimer > 0f)
            _dodgeCdTimer -= delta;
        if (!IsDodging)
            return;
        _dodgeTimer -= delta;
        if (_dodgeTimer <= 0f)
        {
            IsDodging = false;
            _visualRoot.Basis = Basis.Identity;          // end the roll pose
            _visualRoot.Position = Vector3.Zero;         // restore bob pivot
        }
    }

    private void UpdateStamina(float delta)
    {
        bool draining = IsSprinting;
        if (draining)
        {
            Stamina = Mathf.Max(0f, Stamina - SprintDrain * delta);
            _staminaIdleTimer = StaminaRegenDelay;
        }
        else
        {
            if (_staminaIdleTimer > 0f)
                _staminaIdleTimer -= delta;
            else
                Stamina = Mathf.Min(StaminaMax, Stamina + StaminaRegen * delta);
        }
    }

    private void FaceMovement(float delta)
    {
        var v = Velocity;
        if (v.LengthSquared() < 0.25f)
            return;
        float targetYawRad = Mathf.Atan2(-v.X, -v.Z);  // body -Z faces velocity
        float currentRad = Mathf.DegToRad(RotationDegrees.Y);
        RotationDegrees = new Vector3(0f,
            Mathf.RadToDeg(Mathf.LerpAngle(currentRad, targetYawRad,
                1f - Mathf.Exp(-TurnRate * delta))), 0f);
    }

    // ---------------- Procedural animation (Vision 6.8 fallback) ----------

    private void AnimateLocomotion(float delta)
    {
        float speed = Velocity.Length();
        if (IsOnFloor() && speed > 0.5f)
        {
            _bobPhase += delta * speed * 2.6f;       // stride frequency
            float s = Mathf.Sin(_bobPhase);
            _visualRoot.Position = new Vector3(0f, 0.06f * Mathf.Abs(s), 0f);
            _visualRoot.RotationDegrees = new Vector3(0f, 0f, 4f * s); // sway
            // Lean into the direction of travel (silhouette readability):
            // pitch forward, scaled by speed (body yaw already faces travel).
            _visualRoot.RotationDegrees = new Vector3(
                Mathf.Min(10f, speed * 1.1f),
                _visualRoot.RotationDegrees.Y,
                4f * s);
            if (_bobLastSin * s < 0f)                // zero crossing = step
                EmitSignal(SignalName.Footstep);
            _bobLastSin = s;
        }
        else
        {
            _visualRoot.Position = _visualRoot.Position.Lerp(Vector3.Zero,
                1f - Mathf.Exp(-10f * delta));
            _visualRoot.RotationDegrees = _visualRoot.RotationDegrees.Lerp(
                Vector3.Zero, 1f - Mathf.Exp(-10f * delta));
            _bobLastSin = 0f;
        }
    }

    private void AnimateDodgeSpin(float delta)
    {
        // Roll around the capsule CENTER (0.9 m up), not the feet: pivoting at
        // the feet orbits the capsule 0.9 m INTO the floor at 180 deg (caught
        // by the playtester visual gate). Composite local transform =
        // rotation R about the center c:  p -> c + R*(p - c)  =>  T = R,
        // origin = c - R*c.  Axis is frozen in body space (yaw is locked
        // while dodging), so the tumble reads as a forward roll.
        float t = Mathf.Clamp(1f - _dodgeTimer / DodgeDuration, 0f, 1f);
        var r = new Basis(_rollAxisLocal, Mathf.Lerp(0f, Mathf.Tau, t));
        _visualRoot.Basis = r;
        _visualRoot.Position = CapsuleCenter - r * CapsuleCenter;
    }

    /// <summary>Rich state for the playtester runtime digest (mcp_watch).</summary>
    public Godot.Collections.Dictionary _mcp_state() => new()
    {
        ["stamina"] = Stamina,
        ["sprinting"] = IsSprinting,
        ["dodging"] = IsDodging,
        ["invulnerable"] = IsInvulnerable,
        ["speed"] = Velocity.Length(),
        ["on_floor"] = IsOnFloor(),
        ["move_speed_cap"] = IsSprinting ? SprintSpeed : WalkSpeed,
    };
}
