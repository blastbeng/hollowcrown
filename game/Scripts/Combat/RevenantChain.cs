using Godot;
using System.Collections.Generic;
using Hollowcrown.Core;
using Hollowcrown.Networking;
using Hollowcrown.Player;
using Hollowcrown.World;

namespace Hollowcrown.Combat;

/// <summary>
/// Revenant bone spear (Vision 7): Q — a ground-line projectile; the hitbox
/// is a 9 m x 1.2 m strip from the caster toward the cursor (Vision 6.9).
/// The v1 hit applies on cast (the line IS the hitbox, validated server-side
/// per CombatTables) while the client draws the bone bolt travelling the
/// line — gameplay-identical, BALANCE.md documents the simplification.
/// SERVER-AUTHORITATIVE (Vision 2.3).
/// </summary>
public partial class RevenantChain : Node3D, IAbilityProvider
{
    [Export] public float Range = 9f;             // BALANCE.md: revenant_spear
    [Export] public float Width = 1.2f;
    [Export] public float Cooldown = 5f;

    private CharacterBody3D _body = null!;
    private Camera3D? _cam;
    private MeshInstance3D? _lineFlash, _bolt;
    private float _cd, _boltTimer, _flashTimer;
    private Vector3 _boltFrom, _boltDir;

    public IEnumerable<AbilitySlot> Slots()
    {
        yield return new AbilitySlot("Q", "Spear", () => Mathf.Max(0f, _cd) / Cooldown, () => false);
    }

    public override void _Ready()
    {
        _body = (CharacterBody3D)GetParent();
        GD.Print("REVENANT CHAIN READY — Q: bone spear, 9m x 1.2m ground line (damage server-computed)");
    }

    public override void _Process(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        _cam ??= GetViewport().GetCamera3D();
        if (_cam is null)
            return;

        if (_cd > 0f) _cd -= delta;

        if (Input.IsActionJustPressed("attack"))
            Cast();

        // Bone bolt travels the hit line for 0.35 s (Vision 6.9 projectile).
        if (_boltTimer > 0f)
        {
            _boltTimer -= delta;
            float t = 1f - Mathf.Max(0f, _boltTimer) / 0.35f;
            if (_bolt is not null)
                _bolt.GlobalPosition = _boltFrom + _boltDir * (Range * Mathf.Min(1f, t));
            if (_boltTimer <= 0f && _bolt is not null)
                _bolt.Visible = false;
        }

        if (_flashTimer > 0f)
        {
            _flashTimer -= delta;
            if (_flashTimer <= 0f && _lineFlash is not null)
                _lineFlash.Visible = false;
        }
    }

    private void Cast()
    {
        if (_cd > 0f || _body is PlayerController pc && (pc.IsDead || pc.IsStunned))
            return;
        _cd = Cooldown;

        Vector3 aim = Aim.CursorGroundPoint(_cam!);
        var toAim = aim - _body.GlobalPosition;
        toAim.Y = 0f;
        if (toAim.LengthSquared() < 0.0001f)
            toAim = -_body.GlobalBasis.Z;
        Vector3 facing = toAim.Normalized();

        _body.RotationDegrees = new Vector3(0f,
            Mathf.RadToDeg(Mathf.Atan2(-facing.X, -facing.Z)), 0f);

        ShowSpear(facing);
        var authority = CombatAuthority.For(this);
        int requests = 0;
        foreach (var node in GetTree().GetNodesInGroup("combat_targets"))
        {
            if (node is not Node3D target || target == _body)
                continue;
            var to = target.GlobalPosition - _body.GlobalPosition;
            to.Y = 0f;
            float along = to.Dot(facing);
            if (along < -0.35f || along > Range + 0.35f)
                continue;
            float lateral = Mathf.Abs(to.Cross(facing).Y);
            if (lateral > Width * 0.5f + 0.35f)
                continue;
            if (node is ICombatTarget combatTarget)
                authority?.RequestHit(combatTarget.CombatId, (int)AttackId.BoneSpear,
                    _body.GlobalPosition, facing);
            requests++;
        }
        GD.Print($"REVENANT SPEAR attack={(int)AttackId.BoneSpear} requests={requests} (server computes damage)");
    }

    private void ShowSpear(Vector3 facing)
    {
        if (_lineFlash is null)
        {
            _lineFlash = new MeshInstance3D
            {
                Name = "SpearLine",
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.42f, 0.29f, 0.54f, 0.35f),   // arcane line
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_lineFlash);
            _bolt = new MeshInstance3D
            {
                Name = "BoneBolt",
                Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.09f, 0.6f) },
                MaterialOverride = MaterialFactory.PlayerAccent(),   // bone
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(_bolt);
        }

        _lineFlash.Mesh = GroundShapes.Line(Range, Width, facing);
        _lineFlash.GlobalPosition = new Vector3(_body.GlobalPosition.X, 0.03f, _body.GlobalPosition.Z);
        _lineFlash.Visible = true;
        _flashTimer = 0.2f;

        _boltFrom = new Vector3(_body.GlobalPosition.X, 0.6f, _body.GlobalPosition.Z);
        _boltDir = facing;
        if (_bolt is not null)
        {
            _bolt.RotationDegrees = new Vector3(0f,
                Mathf.RadToDeg(Mathf.Atan2(-facing.X, -facing.Z)), 0f);
            _bolt.GlobalPosition = _boltFrom;
            _bolt.Visible = true;
            _boltTimer = 0.35f;
        }
    }
}
