using Godot;
using System.Collections.Generic;
using Hollowcrown.Core;
using Hollowcrown.Networking;
using Hollowcrown.Player;

namespace Hollowcrown.Combat;

/// <summary>
/// Nightblade fast dual-dagger chain (Vision 7): Q / LMB, three quick stabs —
/// light, light, heavy finisher. Tighter and shorter than the Warden arc but
/// faster (BALANCE.md: nightblade_chain). The hitbox is a GROUND-PROJECTED
/// arc sector flash (Vision 6.9). SERVER-AUTHORITATIVE (Vision 2.3): damage,
/// range, arc, cooldown and the stealth-break bonus are computed by
/// CombatAuthority (CombatTables).
/// </summary>
public partial class NightbladeChain : Node3D, IAbilityProvider
{
    [Signal] public delegate void ChainSwingEventHandler(int index, Vector3 aimPoint);

    [Export] public float Reach = 2.0f;           // BALANCE.md: nightblade_chain
    [Export] public float ArcDegrees = 100f;
    [Export] public float ComboWindow = 0.7f;

    private const int SwingCount = 3;

    private CharacterBody3D _body = null!;
    private Camera3D? _cam;
    private MeshInstance3D? _arcFlash;
    private int _combo;
    private float _comboTimer, _flashTimer;

    public override void _Ready()
    {
        _body = (CharacterBody3D)GetParent();
        GD.Print("NIGHTBLADE CHAIN READY — Q/LMB: 3 fast dagger stabs, 100deg x 2.0m (damage server-computed)");
    }

    public IEnumerable<AbilitySlot> Slots()
    {
        yield return new AbilitySlot("Q", "Daggers", () => 0f, () => false);
    }

    public override void _Process(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        _cam ??= GetViewport().GetCamera3D();
        if (_cam is null)
            return;

        if (_comboTimer > 0f)
        {
            _comboTimer -= delta;
            if (_comboTimer <= 0f)
                _combo = 0;
        }

        if (Input.IsActionJustPressed("attack"))
            Swing();

        if (_flashTimer > 0f)
        {
            _flashTimer -= delta;
            if (_flashTimer <= 0f && _arcFlash is not null)
                _arcFlash.Visible = false;
        }
    }

    private void Swing()
    {
        if (_body is PlayerController pc && (pc.IsDead || pc.IsStunned))
            return;
        int index = _combo;
        if (_body is PlayerController attacker)
            attacker.PlayAttackAnim(index);       // dagger stab clip (Punch_Jab/Cross)

        Vector3 aim = Aim.CursorGroundPoint(_cam!);
        var toAim = aim - _body.GlobalPosition;
        toAim.Y = 0f;
        if (toAim.LengthSquared() < 0.0001f)
            toAim = -_body.GlobalBasis.Z;
        Vector3 facing = toAim.Normalized();

        _body.RotationDegrees = new Vector3(0f,
            Mathf.RadToDeg(Mathf.Atan2(-facing.X, -facing.Z)), 0f);

        _combo = (_combo + 1) % SwingCount;
        _comboTimer = ComboWindow;

        ShowStab(facing);
        var authority = CombatAuthority.For(this);
        int attackId = index switch
        {
            0 => (int)AttackId.DaggerLight,
            1 => (int)AttackId.DaggerMid,
            _ => (int)AttackId.DaggerFinisher,
        };
        int requests = 0;
        foreach (var node in GetTree().GetNodesInGroup("combat_targets"))
        {
            if (node is not Node3D target || target == _body)
                continue;
            var to = target.GlobalPosition - _body.GlobalPosition;
            to.Y = 0f;
            if (to.Length() > Reach + 0.35f)
                continue;
            if (Mathf.RadToDeg(facing.AngleTo(to.Normalized())) > ArcDegrees * 0.5f)
                continue;
            if (node is ICombatTarget combatTarget)
                authority?.RequestHit(combatTarget.CombatId, attackId,
                    _body.GlobalPosition, facing);
            requests++;
        }
        EmitSignal(SignalName.ChainSwing, index, aim);
        GD.Print($"NIGHTBLADE STAB {index + 1}/3 attack={attackId} requests={requests} (server computes damage)");
    }

    /// <summary>Ground-projected arc flash in cold steel (readability, 6.9).</summary>
    private void ShowStab(Vector3 facing)
    {
        if (_arcFlash is null)
        {
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.77f, 0.8f, 0.86f, 0.30f),   // cold steel flash
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            _arcFlash = new MeshInstance3D
            {
                Name = "StabFlash",
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_arcFlash);
        }

        _arcFlash.Mesh = GroundShapes.Sector(Reach, ArcDegrees, facing);
        _arcFlash.GlobalPosition = new Vector3(_body.GlobalPosition.X, 0.03f, _body.GlobalPosition.Z);
        _arcFlash.Visible = true;
        _flashTimer = 0.12f;
    }
}
