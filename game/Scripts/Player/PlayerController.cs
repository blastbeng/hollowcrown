using Godot;
using Hollowcrown.Combat;
using Hollowcrown.Networking;
using Hollowcrown.World;

namespace Hollowcrown.Player;

/// <summary>
/// Player controller (Vision 1 / 6.8 / 7): WASD movement RELATIVE TO THE ISO
/// CAMERA YAW (rig never free-rotates), sprint on stamina, dodge roll on
/// Space with a 0.3 s i-frame window (Vision 7), rig plays the class model
/// (WardenModel: rigged Quaternius humanoid + real clips per Vision 6.8).
/// Tunables are recorded in BALANCE.md; server-authoritative combat lands
/// with the combat core task and will consume IsInvulnerable.
/// </summary>
public partial class PlayerController : CharacterBody3D, ICombatTarget
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

    /// <summary>Class of THIS body (Vision 7): picks the model variant and
    /// the kit nodes. Set by the arena (character select flow / --class
    /// launch flag for tests) before _Ready runs.</summary>
    [Export] public PlayerClass Class { get; set; } = PlayerClass.Warden;

    /// <summary>Class the NEXT-spawned player uses (set by Main from the
    /// --class launch arg, or later the selected character). Static because
    /// the arena builds the player after the boot screens.</summary>
    public static PlayerClass PendingClass = PlayerClass.Warden;

    // --- Live state (HUD + combat core read these) ---
    public float Stamina { get; private set; }
    public bool IsDodging { get; private set; }
    public bool IsInvulnerable => IsDodging;        // Vision 7: i-frames
    public bool IsSprinting { get; private set; }
    public int PeerId { get; private set; } = 1;    // ENet peer id == combat id

    /// <summary>CombatAuthority assigns the ENet peer id after spawn approval
    /// (boot-time registration uses the offline id 1).</summary>
    public void AssignCombatId(int id) => PeerId = id;

    // --- Server-authoritative combat mirror (ICombatTarget, Vision 2.3) ---
    // The MATCH SERVER owns this HP number; the local value mirrors what the
    // authority broadcasts and never drives gameplay decisions locally.
    public int MaxHp => CombatAuthority.PlayerMaxHp;   // BALANCE.md: player_hp
    public int Hp { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsStunned => _stunTimer > 0f;
    public int CombatId => PeerId;
    public Vector3 CombatPosition => GlobalPosition;
    public string DisplayName => $"{PlayerClassInfo.Label(Class)}#{PeerId}";

    private const float FallDuration = 0.45f;
    private float _stunTimer, _fallTimer;

    public void OnHitApplied(int amount, bool heavy, int hpAfter)
    {
        if (amount <= 0)
            return;
        Hp = hpAfter;
        DamageNumber.Spawn(this, GlobalPosition, amount, heavy);
        _model?.PlayHit();                     // rigged hit reaction (Vision 6.8)
    }

    /// <summary>Authority-broadcast stun (shield bash): movement locked while
    /// the timer runs; casting is gated in the kits.</summary>
    public void OnStunned(float seconds)
    {
        if (IsDead || seconds <= 0f)
            return;
        _stunTimer = Mathf.Max(_stunTimer, seconds);
        GD.Print($"{PlayerClassInfo.Label(Class).ToUpperInvariant()} STUNNED {seconds:0.00}s (authority)");
    }

    /// <summary>Authority-broadcast stealth state (nightblade): the local
    /// body ghosts while stealthed; the +50% next-hit bonus is server-side.</summary>
    public void OnStealthed(bool stealthed)
    {
        IsStealthed = stealthed;
        _model?.SetGhost(stealthed ? 0.35f : 1f);
        GD.Print($"{PlayerClassInfo.Label(Class)} STEALTH {(stealthed ? "ON (ghosted)" : "OFF")}");
    }

    public void OnKilled()
    {
        IsDead = true;
        Velocity = Vector3.Zero;
        _stunTimer = 0f;
        _fallTimer = FallDuration;
        GD.Print($"WARDEN DOWN ({DisplayName}) — respawn scheduled server-side");
    }

    public void OnRespawned(int hpAfter, Vector3 spawnPos)
    {
        IsDead = false;
        Hp = hpAfter;
        _model?.ResetPose();
        GlobalPosition = spawnPos;
        Velocity = Vector3.Zero;
        GD.Print($"WARDEN RESPAWNED ({DisplayName}) at {spawnPos} (authority)");
    }

    // --- Shield wall + warcry buffs (Vision 7 Warden kit; WardenKit drives) ---
    [Export] public float WallStaminaDrain = 25f;   // BALANCE.md: wall_drain
    public bool IsShieldWalling { get; private set; }
    public bool IsStealthed { get; private set; }
    private float _wallTimer;
    private float _warcryTimer;
    private float _warcryMultiplier = 1f;
    private CanvasLayer? _blindOverlay;             // inside an enemy smoke zone (Vision 7)

    // --- Rigged class model (Vision 6.8): real clips, retinted, weapon sockets.
    private Node3D _visualRoot = null!;   // presentation pivot (model child)
    private WardenModel? _model;
    private float _dodgeTimer, _dodgeCdTimer, _staminaIdleTimer;
    private Vector3 _lastMoveDir = Vector3.Forward;
    private Vector3 _dodgeDir;
    private Camera3D? _cam;

    /// <summary>Class chains call this per chain step so the model's attack
    /// clip matches the ground-projected hitbox flash.</summary>
    public void PlayAttackAnim(int chainIndex = 0) => _model?.PlayAttack(chainIndex);

    public override void _Ready()
    {
        // Rigged class model (Vision 6.8): the capsule stand-in is retired.
        _visualRoot = new Node3D { Name = "VisualRoot" };
        _model = new WardenModel { Name = "Model", ClassVariant = Class };
        _visualRoot.AddChild(_model);
        AddChild(_visualRoot);

        // Class kit nodes (Vision 7): the chain + kit belong to the BODY so
        // input, stamina and HUD slots all live on the same owner.
        switch (Class)
        {
            case PlayerClass.Nightblade:
                AddChild(new NightbladeChain { Name = "Chain" });
                AddChild(new NightbladeKit { Name = "Kit" });
                break;
            default:
                AddChild(new WardenChain { Name = "Chain" });
                AddChild(new WardenKit { Name = "Kit" });
                break;
        }

        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f },
            Position = new Vector3(0f, 0.9f, 0f),
        });

        FloorSnapLength = 0.4f;
        Stamina = StaminaMax;
        Hp = MaxHp;
        AddToGroup("combat_targets");   // kit candidate set (dummies + players)
        CombatAuthority.For(this)?.RegisterSelf(this);   // join the authority's world
        GD.Print($"PLAYER CONTROLLER READY — {PlayerClassInfo.Label(Class)}: WASD camera-relative, sprint, dodge roll (0.3 s i-frames)");
    }

    public override void _ExitTree()
    {
        // A freed player must not stay in the authority's world (stale target
        // would eat validation results).
        if (CombatId > 0)
            CombatAuthority.For(this)?.Unregister(CombatId);
    }

    public override void _PhysicsProcess(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        _cam = GetViewport().GetCamera3D() ?? _cam;

        if (IsDead)
        {
            UpdateDeathVisual(delta);      // stays down until the authority respawns us
            return;
        }
        bool stunned = _stunTimer > 0f;
        if (stunned)
            _stunTimer -= delta;

        UpdateStamina(delta);
        UpdateDodgeTimers(delta);
        if (!stunned)
            HandleDodgeInput();
        UpdateBuffTimers(delta);
        UpdateBlindOverlay();

        var input = stunned
            ? Vector2.Zero
            : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var moveDir = CameraRelative(input);

        if (IsDodging)
        {
            Velocity = _dodgeDir * DodgeSpeed;    // burst, no steering
        }
        else
        {
            if (moveDir.LengthSquared() > 0.01f)
                _lastMoveDir = moveDir;

            bool wantsSprint = Input.IsActionPressed("sprint")
                               && moveDir.LengthSquared() > 0.01f;
            IsSprinting = wantsSprint && Stamina > 1f && !IsShieldWalling;   // no sprint behind the shield

            var goal = moveDir * (IsSprinting ? SprintSpeed : WalkSpeed);
            Velocity = Velocity.Lerp(goal, 1f - Mathf.Exp(-Accel * delta));
            if (!IsOnFloor())
                Velocity += Vector3.Down * Gravity * delta;

            FaceMovement(delta);
            AnimateLocomotion(delta);
        }

        MoveAndSlide();
    }

    /// <summary>Death presentation: the rigged Death01 clip plays once and the
    /// body fades toward the respawn (authority-broadcast). Nothing else moves
    /// while down.</summary>
    private void UpdateDeathVisual(float delta)
    {
        if (_fallTimer > 0f)
        {
            _fallTimer -= delta;
            float t = 1f - Mathf.Max(0f, _fallTimer) / FallDuration;
            _model?.PlayDeath(t);
        }
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
        _dodgeTimer = DodgeDuration;
        _dodgeCdTimer = DodgeCooldown;
        IsDodging = true;
        _model?.PlayRoll(DodgeDuration);       // rigged roll clip (Vision 6.8)
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
        }
    }

    private void UpdateStamina(float delta)
    {
        bool draining = IsSprinting || IsShieldWalling;
        if (draining)
        {
            Stamina = Mathf.Max(0f, Stamina - (IsShieldWalling ? WallStaminaDrain : SprintDrain) * delta);
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

    /// <summary>Spend stamina for an ability (Warden bash). Fails cleanly.</summary>
    public bool TrySpendStamina(float cost)
    {
        if (Stamina < cost)
            return false;
        Stamina -= cost;
        _staminaIdleTimer = StaminaRegenDelay;
        return true;
    }

    /// <summary>Vision 7: shield wall = 100% block for the duration; the
    /// stamina drain lives in UpdateStamina and ends it at empty.</summary>
    public void StartShieldWall(float duration)
    {
        if (IsDodging)
            return;
        _wallTimer = duration;
        IsShieldWalling = true;
    }

    /// <summary>Vision 7: warcry = timed ally buff; self-buff while solo.</summary>
    public void StartWarcry(float duration, float multiplier)
    {
        _warcryTimer = duration;
        _warcryMultiplier = multiplier;
    }

    /// <summary>Chain damage multiplier while a warcry buff is running.</summary>
    public float DamageMultiplier => _warcryTimer > 0f ? _warcryMultiplier : 1f;

    private void UpdateBuffTimers(float delta)
    {
        if (IsShieldWalling)
        {
            _wallTimer -= delta;
            if (_wallTimer <= 0f || Stamina <= 0f)
                IsShieldWalling = false;              // Vision 7: wall ends at 2 s or empty stamina
        }
        if (_warcryTimer > 0f)
            _warcryTimer -= delta;
    }

    /// <summary>Blind (Vision 7 nightblade smoke): standing inside an enemy
    /// smoke zone darkens the screen. The zone itself also REJECTS hits
    /// server-side — this overlay is what the blinded player feels.</summary>
    private void UpdateBlindOverlay()
    {
        bool blinded = SmokeZone.AnyZoneContains(this, GlobalPosition);
        if (blinded && _blindOverlay is null)
        {
            _blindOverlay = new CanvasLayer { Layer = 5 };
            var rect = new ColorRect
            {
                Color = new Color(0.02f, 0.02f, 0.03f, 0.6f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _blindOverlay.AddChild(rect);
            AddChild(_blindOverlay);
        }
        if (_blindOverlay is not null)
            _blindOverlay.Visible = blinded;
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

    // ---------------- Rigged model locomotion (Vision 6.8) ----------------

    private void AnimateLocomotion(float delta)
    {
        // Real clips (Idle / Walk / Jog_Fwd) scale their stride to the actual
        // velocity; the model handles its own bob/footfall.
        _model?.PlayLocomotion(Velocity.Length(), IsSprinting);
    }

    /// <summary>Rich state for the playtester runtime digest (mcp_watch).</summary>
    public Godot.Collections.Dictionary _mcp_state() => new()
    {
        ["hp"] = Hp,
        ["max_hp"] = MaxHp,
        ["dead"] = IsDead,
        ["stunned"] = IsStunned,
        ["peer_id"] = PeerId,
        ["class"] = PlayerClassInfo.Label(Class),
        ["stealthed"] = IsStealthed,
        ["stamina"] = Stamina,
        ["sprinting"] = IsSprinting,
        ["dodging"] = IsDodging,
        ["invulnerable"] = IsInvulnerable,
        ["speed"] = Velocity.Length(),
        ["on_floor"] = IsOnFloor(),
        ["move_speed_cap"] = IsSprinting ? SprintSpeed : WalkSpeed,
        ["shield_walling"] = IsShieldWalling,
        ["damage_multiplier"] = DamageMultiplier,
    };
}
