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

    /// <summary>Class id from the realm handshake — picks the enemy model
    /// variant (twin daggers, staff + hood) so classes read at iso zoom.</summary>
    public string ClassId { get; set; } = "warden";

    public int MaxHp => _maxHp;
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

    // Status visuals (remote-avatar polish, NEXT 1): authority mirrors with
    // no server timer — the puppet ticks them in _Process.
    private float _rootTimer, _stunFlashTimer;
    private float _wardAmount;
    private MeshInstance3D? _rootDisc, _stunFlash, _wardDisc;
    // Cast telegraph (NEXT 1, Vision 6.9): the ground shape flash for remote
    // swings/casts — same look as the local chains' hitbox flashes.
    private MeshInstance3D? _castFlash;
    private float _castFlashTimer;
    // Nameplate HP bar (NEXT 1): thin billboarded slab over the head — remote
    // avatars are the only bodies without a HUD row of their own.
    private Node3D _hpBarRoot = null!;
    private MeshInstance3D _hpBarBack = null!, _hpBarFill = null!;
    // Server-declared max HP (handshake vitality); base until SpawnPlayerRpc
    // sets the real number (defaults keep offline/test avatars correct).
    private int _maxHp = CombatAuthority.PlayerMaxHp;

    private const float BarWidth = 1.2f;

    public override void _Ready()
    {
        AddToGroup("combat_targets");   // kit candidate set (dummies + players)
        _targetPos = Position;
        _lastPos = Position;
        Hp = MaxHp;

        _visual = new Node3D { Name = "Visual" };
        // Rigged enemy model (Vision 6.8): same class pipeline, colder tint so
        // friend vs foe reads at iso zoom. The capsule stand-in is retired.
        _model = new WardenModel
        {
            Name = "Model",
            EnemyTint = true,
            ClassVariant = PlayerClassInfo.FromId(ClassId),
        };
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

        // Nameplate HP bar (NEXT 1): thin slab ABOVE the nameplate, yawed to
        // face the iso camera (meshes cannot billboard). Fill scales from the
        // LEFT edge via position compensation — the box pivots at center.
        var backMesh = new BoxMesh { Size = new Vector3(BarWidth, 0.08f, 0.03f) };
        _hpBarBack = new MeshInstance3D
        {
            Name = "HpBack",
            Mesh = backMesh,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color("121014"),           // palette: near-black slab
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        // Unit-width fill: X scale IS the health fraction; position keeps the
        // left edge pinned (center-pivot compensation). Full scale spans the
        // full BarWidth backing slab.
        _hpBarFill = new MeshInstance3D
        {
            Name = "HpFill",
            Mesh = new BoxMesh { Size = new Vector3(BarWidth, 0.06f, 0.026f) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color("c0392b"),            // blood red (enemy palette)
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, 0.005f, 0.005f),
        };
        _hpBarRoot = new Node3D { Name = "HpBar", Position = new Vector3(0f, 2.55f, 0f) };
        _hpBarRoot.AddChild(_hpBarBack);
        _hpBarRoot.AddChild(_hpBarFill);
        AddChild(_hpBarRoot);
        SyncHpBar();                          // full bar until the first hit lands

        GD.Print($"REMOTE AVATAR READY — {DisplayName} ({PlayerClassInfo.Label(PlayerClassInfo.FromId(ClassId))} model, red nameplate)");
    }

    /// <summary>Authority-relayed state (10 Hz): the puppet lerps to it.</summary>
    public void SetNetworkTransform(Vector3 pos, float yaw)
    {
        _targetPos = pos;
        _targetYaw = yaw;
    }

    /// <summary>Server-declared max HP (remote-peer gear gap, NEXT 1): called
    /// by SpawnPlayerRpc after AddChild (the bar nodes exist) — vitality-affix
    /// victims no longer mirror base 100. Arrives before any hit can land, so
    /// the mirror snaps to full.</summary>
    public void SetMaxHp(int maxHp)
    {
        _maxHp = Mathf.Max(1, maxHp);
        if (!IsDead)
            Hp = _maxHp;
        SyncHpBar();
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

        // HP bar (NEXT 1): meshes cannot billboard — set the GLOBAL yaw to
        // face the iso camera (the avatar body itself yaws for casts, so a
        // local rotation would inherit it).
        if (GetViewport().GetCamera3D() is { } cam)
        {
            var toCam = cam.GlobalPosition - GlobalPosition;
            toCam.Y = 0f;
            if (toCam.LengthSquared() > 0.0001f)
                _hpBarRoot.GlobalRotation = new Vector3(0f,
                    Mathf.Atan2(-toCam.X, -toCam.Z), 0f);
        }

        // Root disc (NEXT 1): arcane circle while the authority timer runs.
        if (_rootTimer > 0f)
        {
            _rootTimer -= delta;
            if (_rootDisc is null)
            {
                _rootDisc = new MeshInstance3D
                {
                    Name = "RootDisc",
                    Mesh = GroundShapes.Sector(1.1f, 360f, Vector3.Forward),
                    MaterialOverride = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.42f, 0.29f, 0.54f, 0.5f),   // arcane
                        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                AddChild(_rootDisc);
            }
            _rootDisc.Visible = true;
            _rootDisc.GlobalPosition = new Vector3(GlobalPosition.X, 0.02f, GlobalPosition.Z);
        }
        else if (_rootDisc is not null && _rootDisc.Visible)
            _rootDisc.Visible = false;

        // Stun flash ring (NEXT 1): brief bone-colored pulse under the body.
        if (_stunFlashTimer > 0f)
        {
            _stunFlashTimer -= delta;
            if (_stunFlash is null)
            {
                _stunFlash = new MeshInstance3D
                {
                    Name = "StunFlash",
                    Mesh = GroundShapes.Sector(1.1f, 360f, Vector3.Forward),
                    MaterialOverride = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.85f, 0.81f, 0.75f, 0.5f),   // bone #d8cfc0
                        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                AddChild(_stunFlash);
            }
            _stunFlash.Visible = true;
            _stunFlash.GlobalPosition = new Vector3(GlobalPosition.X, 0.025f, GlobalPosition.Z);
        }
        else if (_stunFlash is not null && _stunFlash.Visible)
            _stunFlash.Visible = false;

        // Ward disc (NEXT 1): mirrors the local RevenantKit.SyncWardDisc look.
        if (_wardDisc is not null)
        {
            bool warded = _wardAmount > 0f;
            _wardDisc.Visible = warded;
            if (warded)
                _wardDisc.GlobalPosition = new Vector3(GlobalPosition.X, 0.04f, GlobalPosition.Z);
        }

        // Cast telegraph flash (NEXT 1): brief ground shape under the swing.
        if (_castFlashTimer > 0f)
        {
            _castFlashTimer -= delta;
            if (_castFlashTimer <= 0f && _castFlash is not null)
                _castFlash.Visible = false;
        }
    }

    // --------------------------- remote cast replay -------------------------

    /// <summary>Authority-cast relay (NEXT 1): replays the remote peer's
    /// swing/cast on this puppet — attack clip per chain step (the model maps
    /// Nightblade to Punch_Jab/Cross, Revenant to Spell_Simple_Shoot) + the
    /// ground telegraph shape from the attack table. The local player's own
    /// client never gets here (CastRpc skips the local body).</summary>
    public void PlayRemoteCast(int attackId, Vector3 facing)
    {
        if (IsDead)
            return;
        // Face the cast so the silhouette reads the swing (chains do the
        // same). _targetYaw is RADIANS (the position relay stores radians);
        // the body snaps instantly, the position relay keeps lerping on top.
        _targetYaw = Mathf.Atan2(-facing.X, -facing.Z);
        RotationDegrees = new Vector3(0f, Mathf.RadToDeg(_targetYaw), 0f);
        _model?.PlayAttack(ChainIndexFromAttackId(attackId));

        var atk = CombatTables.Get(attackId);
        if (_castFlash is null)
        {
            _castFlash = new MeshInstance3D
            {
                Name = "CastFlash",
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.69f, 0.55f, 0.34f, 0.35f),  // accent, 35%
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_castFlash);
        }
        // Shape mirrors the hitbox: arcs for the melee chain/bash, the ground
        // line for spear/drain (WardenChain.ShowArc pattern).
        _castFlash.Mesh = atk.Shape == AttackShape.Line
            ? GroundShapes.Line(atk.Range, atk.Width, facing)
            : GroundShapes.Sector(atk.Range, atk.ArcDegrees, facing);
        _castFlash.GlobalPosition = new Vector3(GlobalPosition.X, 0.03f, GlobalPosition.Z);
        // The mesh geometry is baked in WORLD space (GroundShapes faces it at
        // the world `facing`); zero the GLOBAL rotation so the body's cast
        // yaw does not rotate it a second time.
        _castFlash.GlobalRotation = Vector3.Zero;
        _castFlash.Visible = true;
        _castFlashTimer = 0.15f;
        GD.Print($"REMOTE AVATAR CAST — {DisplayName} attack={attackId} (relay)");
    }

    /// <summary>Chain-step index for the attack clip: chains map 1/2/3 to
    /// swing 1/2/finisher, kits cast with index 0 (Spell clip / bash).</summary>
    private static int ChainIndexFromAttackId(int attackId) => attackId switch
    {
        (int)AttackId.ChainMid => 1,
        (int)AttackId.DaggerMid => 1,
        (int)AttackId.ChainFinisher => 2,
        (int)AttackId.DaggerFinisher => 2,
        _ => 0,   // ChainLight/DaggerLight/ShieldBash/BoneSpear/GraveGrasp/DrainTick
    };

    // ------------------- authority mirrors (all peers) ---------------------

    public void AssignCombatId(int id) => PeerId = id;

    public void OnHitApplied(int amount, bool heavy, int hpAfter)
    {
        if (amount <= 0)
            return;
        Hp = hpAfter;
        SyncHpBar();
        DamageNumber.Spawn(this, GlobalPosition, amount, heavy);
        _punchTimer = 0.12f;
        _model?.PlayHit();
    }

    public void OnStunned(float seconds)
    {
        // Stun flash ring (NEXT 1): brief bone pulse — minimal, not the full
        // stagger rig the local body gets.
        _stunFlashTimer = Mathf.Min(seconds, 0.5f);
    }

    public void OnStealthed(bool stealthed)
    {
        // Stealthed enemy puppets ghost too — invisibility reads on screen.
        _model?.SetGhost(stealthed ? 0.35f : 1f);
    }

    public void OnRooted(float seconds)
    {
        // Root disc (NEXT 1): the server keeps the timer; the puppet draws a
        // flat arcane circle for its duration.
        if (seconds <= 0f || IsDead)
            return;
        _rootTimer = Mathf.Max(_rootTimer, seconds);
        GD.Print($"REMOTE AVATAR ROOTED {seconds:0.0}s — {DisplayName}");
    }

    public void OnWard(float amount)
    {
        // Ward disc (NEXT 1): arcane circle under the puppet while the
        // absorb pool is up (mirrors the local RevenantKit disc).
        _wardAmount = Mathf.Max(0f, amount);
        if (_wardAmount > 0f && _wardDisc is null)
        {
            _wardDisc = new MeshInstance3D
            {
                Name = "WardDisc",
                Mesh = GroundShapes.Sector(1.1f, 360f, Vector3.Forward),
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.42f, 0.29f, 0.54f, 0.5f),   // arcane
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_wardDisc);
        }
        GD.Print($"REMOTE AVATAR WARD {_wardAmount:0} — {DisplayName}");
    }

    public void OnHealed(int hpAfter)
    {
        Hp = Mathf.Clamp(hpAfter, 0, MaxHp);   // puppet mirror of authority HP
        SyncHpBar();
    }

    public void OnKilled()
    {
        IsDead = true;
        _fallTimer = FallDuration;
        // Status visuals die with the body (a running root timer would
        // otherwise reappear mid-respawn).
        _rootTimer = 0f;
        _stunFlashTimer = 0f;
        _wardAmount = 0f;
        if (_rootDisc is not null) _rootDisc.Visible = false;
        if (_stunFlash is not null) _stunFlash.Visible = false;
        if (_wardDisc is not null) _wardDisc.Visible = false;
        if (_castFlash is not null) _castFlash.Visible = false;
        _castFlashTimer = 0f;
        GD.Print($"REMOTE AVATAR DOWN — {DisplayName} (authority)");
    }

    public void OnRespawned(int hpAfter, Vector3 spawnPos)
    {
        IsDead = false;
        Hp = hpAfter;
        SyncHpBar();
        _model?.ResetPose();
        _visual.RotationDegrees = Vector3.Zero;
        _targetPos = spawnPos;
        GlobalPosition = spawnPos;                 // respawn teleports
        _lastPos = spawnPos;
        GD.Print($"REMOTE AVATAR RESPAWNED — {DisplayName} at {spawnPos}");
    }

    /// <summary>HP bar fill (NEXT 1): X scale == health fraction, the fill's
    /// LEFT edge stays pinned (center-pivot compensation: shift right by the
    /// shrinking width's half).</summary>
    private void SyncHpBar()
    {
        float frac = MaxHp > 0 ? Mathf.Clamp((float)Hp / MaxHp, 0f, 1f) : 0f;
        _hpBarFill.Scale = new Vector3(Mathf.Max(frac, 0.001f), 1f, 1f);
        _hpBarFill.Position = new Vector3(-BarWidth * 0.5f * (1f - frac), 0.005f, 0.005f);
    }

    public void OnProgress(int kills, int xp)
    {
        // Puppets don't own progression — the owning client mirrors its own.
    }

    public void OnMmr(long winnerCharacterId, long loserCharacterId, int winnerMmr,
        int loserMmr, int winnerDelta, int loserDelta, string winnerTier, string loserTier)
    {
        // Puppets don't own MMR — the owning client mirrors its own session.
    }
}
