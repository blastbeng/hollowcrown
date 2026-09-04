using System.Collections.Generic;
using Godot;
using Hollowcrown.Core;
using Hollowcrown.Networking;
using Hollowcrown.Player;

namespace Hollowcrown.Combat;

/// <summary>
/// Revenant kit (Vision 7): grave grasp (E — 1 s root circle at the cursor),
/// life drain (R — 2 s channel: 4 x 8 dmg ticks along a 6 m line, caster
/// heals 50% server-side), soul ward (F — absorb pool of 40, server-owned).
/// All numbers are CombatTables/BALANCE.md; this node draws the VFX, gates
/// its own costs/cooldowns and requests the effects.
/// </summary>
public partial class RevenantKit : Node3D, IAbilityProvider
{
    // --- Grave grasp (BALANCE.md: revenant_grasp) ---
    [Export] public float GraspRadius = 4.5f;
    [Export] public float GraspRootSeconds = 1.0f;  // server-applied
    [Export] public float GraspStaminaCost = 20f;
    [Export] public float GraspCooldown = 9f;

    // --- Life drain (BALANCE.md: revenant_drain) ---
    [Export] public float DrainRange = 6f;
    [Export] public float DrainWidth = 1.2f;
    [Export] public float DrainChannel = 2f;        // 4 ticks at 0.5 s
    [Export] public float DrainTickInterval = 0.5f;
    [Export] public float DrainStaminaCost = 20f;
    [Export] public float DrainCooldown = 12f;

    // --- Soul ward (BALANCE.md: revenant_ward) ---
    [Export] public float WardCooldown = CombatTables.SoulWardCooldown;

    private CharacterBody3D _body = null!;
    private PlayerController _pc = null!;
    private Camera3D? _cam;
    private MeshInstance3D? _graspFlash, _drainLine, _wardDisc;
    private float _graspCd, _flashTimer, _drainCd, _drainTimer, _drainTickTimer, _wardCd;
    private Vector3 _drainFacing = Vector3.Forward;

    public IEnumerable<AbilitySlot> Slots()
    {
        yield return new AbilitySlot("E", "Grasp",
            () => Mathf.Max(0f, _graspCd) / GraspCooldown, () => false);
        yield return new AbilitySlot("R", "Drain",
            () => Mathf.Max(0f, Mathf.Max(_drainCd, _drainTimer)) / DrainCooldown,
            () => _drainTimer > 0f);
        yield return new AbilitySlot("F", "Ward",
            () => Mathf.Max(0f, _wardCd) / WardCooldown,
            () => _pc.WardAmount > 0f);
    }

    public override void _Ready()
    {
        _body = (CharacterBody3D)GetParent();
        _pc = (PlayerController)_body;
        GD.Print("REVENANT KIT READY — E grave grasp (1s root), R life drain (2s channel, 50% leech), F soul ward (40 absorb)");
    }

    public override void _Process(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        _cam ??= GetViewport().GetCamera3D();
        if (_cam is null)
            return;

        if (_graspCd > 0f) _graspCd -= delta;
        if (_drainCd > 0f) _drainCd -= delta;
        if (_wardCd > 0f) _wardCd -= delta;

        if (Input.IsActionJustPressed("rev_grasp"))
            GraveGrasp();
        if (Input.IsActionJustPressed("rev_drain"))
            StartDrain();
        if (Input.IsActionJustPressed("rev_ward"))
            SoulWard();

        if (_flashTimer > 0f)
        {
            _flashTimer -= delta;
            if (_flashTimer <= 0f && _graspFlash is not null)
                _graspFlash.Visible = false;
        }
        TickDrain(delta);
        SyncWardDisc();
    }

    // ------------------------------- life drain ----------------------------

    private void StartDrain()
    {
        if (_drainCd > 0f || _drainTimer > 0f || _pc.IsDodging || _pc.IsDead ||
            _pc.IsStunned || !_pc.TrySpendStamina(DrainStaminaCost))
            return;
        _drainCd = DrainCooldown;
        _drainTimer = DrainChannel;
        _drainTickTimer = 0f;   // first tick fires this frame

        Vector3 aim = Aim.CursorGroundPoint(_cam!);
        var toAim = aim - _body.GlobalPosition;
        toAim.Y = 0f;
        if (toAim.LengthSquared() < 0.0001f)
            toAim = -_body.GlobalBasis.Z;
        _drainFacing = toAim.Normalized();
        ShowDrainLine();
        GD.Print("REVENANT DRAIN channel start — 4 x 8 dmg ticks, 50% leech (server-computed)");
    }

    private void TickDrain(float delta)
    {
        if (_drainTimer <= 0f)
        {
            if (_drainLine is not null && _drainLine.Visible)
                _drainLine.Visible = false;
            return;
        }
        _drainTimer -= delta;
        _drainTickTimer -= delta;
        if (_drainTickTimer > 0f)
            return;
        _drainTickTimer = DrainTickInterval;
        if (_pc.IsDead || _pc.IsStunned)
        {
            _drainTimer = 0f;   // channel broken
            return;
        }

        ShowDrainLine();
        var authority = CombatAuthority.For(this);
        int requests = 0;
        foreach (var node in GetTree().GetNodesInGroup("combat_targets"))
        {
            if (node is not Node3D target || target == _body)
                continue;
            var to = target.GlobalPosition - _body.GlobalPosition;
            to.Y = 0f;
            if (to.Length() > DrainRange + 0.35f)
                continue;
            float lateral = Mathf.Abs(to.Cross(_drainFacing).Y);
            if (lateral > DrainWidth * 0.5f + 0.35f)
                continue;
            if (node is ICombatTarget combatTarget)
                authority?.RequestHit(combatTarget.CombatId, (int)AttackId.DrainTick,
                    _body.GlobalPosition, _drainFacing);
            requests++;
        }
        GD.Print($"REVENANT DRAIN tick requests={requests}");
    }

    private void ShowDrainLine()
    {
        if (_drainLine is null)
        {
            _drainLine = new MeshInstance3D
            {
                Name = "DrainLine",
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.42f, 0.29f, 0.54f, 0.45f),   // arcane
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_drainLine);
        }
        _drainLine.Mesh = GroundShapes.Line(DrainRange, DrainWidth, _drainFacing);
        _drainLine.GlobalPosition = new Vector3(_body.GlobalPosition.X, 0.03f, _body.GlobalPosition.Z);
        _drainLine.Visible = true;
    }

    // ------------------------------ soul ward ------------------------------

    private void SoulWard()
    {
        if (_wardCd > 0f || _pc.IsDead || _pc.IsStunned)
            return;
        _wardCd = WardCooldown;
        CombatAuthority.For(this)?.RequestWard();   // server validates + broadcasts
        GD.Print("REVENANT WARD requested (server-owned absorb pool)");
    }

    private void SyncWardDisc()
    {
        bool active = _pc.WardAmount > 0f;
        if (active && _wardDisc is null)
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
        if (_wardDisc is not null)
        {
            _wardDisc.Visible = active;
            if (active)
                _wardDisc.GlobalPosition = new Vector3(
                    _body.GlobalPosition.X, 0.04f, _body.GlobalPosition.Z);
        }
    }

    private void GraveGrasp()
    {
        if (_graspCd > 0f || _pc.IsDodging || _pc.IsDead || _pc.IsStunned ||
            !_pc.TrySpendStamina(GraspStaminaCost))
            return;
        _graspCd = GraspCooldown;

        Vector3 aim = Aim.CursorGroundPoint(_cam!);
        var toAim = aim - _body.GlobalPosition;
        toAim.Y = 0f;
        if (toAim.LengthSquared() < 0.0001f)
            toAim = -_body.GlobalBasis.Z;
        Vector3 facing = toAim.Normalized();

        ShowGraspCircle(toAim);
        var authority = CombatAuthority.For(this);
        int requests = 0;
        foreach (var node in GetTree().GetNodesInGroup("combat_targets"))
        {
            if (node is not Node3D target || target == _body)
                continue;
            var to = target.GlobalPosition - _body.GlobalPosition;
            to.Y = 0f;
            if (to.Length() > GraspRadius + 0.35f)
                continue;
            if (node is ICombatTarget combatTarget)
                authority?.RequestHit(combatTarget.CombatId, (int)AttackId.GraveGrasp,
                    _body.GlobalPosition, facing);
            requests++;
        }
        GD.Print($"REVENANT GRASP root={GraspRootSeconds:0.0}s requests={requests} (server applies the root)");
    }

    /// <summary>Ground circle flash at the CURSOR (the grasp is a zone, not
    /// a self-centered sector — Vision 6.9).</summary>
    private void ShowGraspCircle(Vector3 offset)
    {
        if (_graspFlash is null)
        {
            _graspFlash = new MeshInstance3D
            {
                Name = "GraspFlash",
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.42f, 0.29f, 0.54f, 0.4f),   // arcane
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_graspFlash);
        }
        _graspFlash.Mesh = GroundShapes.Sector(GraspRadius, 360f, Vector3.Forward);
        _graspFlash.GlobalPosition = new Vector3(
            _body.GlobalPosition.X + offset.X, 0.03f, _body.GlobalPosition.Z + offset.Z);
        _graspFlash.Visible = true;
        _flashTimer = 0.25f;
    }

    /// <summary>Rich state for the playtester runtime digest (mcp_watch).</summary>
    public Godot.Collections.Dictionary _mcp_state() => new()
    {
        ["grasp_cd"] = Mathf.Max(0f, _graspCd),
        ["drain_cd"] = Mathf.Max(0f, _drainCd),
        ["draining"] = _drainTimer > 0f,
        ["ward_cd"] = Mathf.Max(0f, _wardCd),
    };
}
