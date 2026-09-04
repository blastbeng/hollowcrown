using System.Collections.Generic;
using Godot;
using Hollowcrown.Core;
using Hollowcrown.Networking;
using Hollowcrown.Player;

namespace Hollowcrown.Combat;

/// <summary>
/// Revenant kit, slice 1 (Vision 7): grave grasp (E — 1 s root circle at the
/// cursor). Life drain + soul ward land in slice 2. The root is
/// SERVER-AUTHORITATIVE: rooted bodies cannot move but CAN still fight.
/// </summary>
public partial class RevenantKit : Node3D, IAbilityProvider
{
    // --- Grave grasp (BALANCE.md: revenant_grasp) ---
    [Export] public float GraspRadius = 4.5f;
    [Export] public float GraspRootSeconds = 1.0f;  // server-applied
    [Export] public float GraspStaminaCost = 20f;
    [Export] public float GraspCooldown = 9f;

    private CharacterBody3D _body = null!;
    private PlayerController _pc = null!;
    private Camera3D? _cam;
    private MeshInstance3D? _graspFlash;
    private float _graspCd, _flashTimer;

    public IEnumerable<AbilitySlot> Slots()
    {
        yield return new AbilitySlot("E", "Grasp",
            () => Mathf.Max(0f, _graspCd) / GraspCooldown, () => false);
        // R drain / F ward land with the revenant slice 2.
    }

    public override void _Ready()
    {
        _body = (CharacterBody3D)GetParent();
        _pc = (PlayerController)_body;
        GD.Print("REVENANT KIT READY — E grave grasp (1s root circle); drain/ward land in slice 2");
    }

    public override void _Process(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        _cam ??= GetViewport().GetCamera3D();
        if (_cam is null)
            return;

        if (_graspCd > 0f) _graspCd -= delta;

        if (Input.IsActionJustPressed("rev_grasp"))
            GraveGrasp();

        if (_flashTimer > 0f)
        {
            _flashTimer -= delta;
            if (_flashTimer <= 0f && _graspFlash is not null)
                _graspFlash.Visible = false;
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
    };
}
