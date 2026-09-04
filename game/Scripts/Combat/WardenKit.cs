using Godot;
using System.Collections.Generic;
using Hollowcrown.Core;
using Hollowcrown.Networking;
using Hollowcrown.Player;

namespace Hollowcrown.Combat;

/// <summary>
/// Warden kit completion (Vision 7): shield bash (E — cone, 0.5 s stun),
/// warcry (R — ally buff radius; self-buff while solo), shield wall (F —
/// 100% block for 2 s, stamina drain). Bash hitbox is a GROUND-PROJECTED
/// cone flash flat on the floor (Vision 6.9); buff/block read as ground
/// decals, not floating UI. Input note: Vision 1 lists QWER, but W is
/// movement — skills bind to Q / E / R / F (BALANCE.md records it).
/// SERVER-AUTHORITATIVE (Vision 2.3): bash damage/stun and the warcry buff
/// are computed server-side by CombatAuthority (CombatTables); this node
/// draws the VFX, gates its own costs/cooldowns, and requests the hits.
/// </summary>
public partial class WardenKit : Node3D, IAbilityProvider
{
    // --- Shield bash (BALANCE.md: warden_bash) ---
    [Export] public float BashRadius = 3.2f;
    [Export] public float BashArcDegrees = 90f;
    [Export] public float BashStunSeconds = 0.5f;   // Vision 7: 0.5 s stun (server-applied)
    [Export] public float BashStaminaCost = 20f;
    [Export] public float BashCooldown = 6f;

    // --- Warcry (BALANCE.md: warden_warcry) ---
    [Export] public float WarcryRadius = 8f;
    [Export] public float WarcryDuration = 10f;
    [Export] public float WarcryBuffMultiplier = 1.15f;   // +15% chain damage
    [Export] public float WarcryCooldown = 12f;

    // --- Shield wall (BALANCE.md: warden_wall) ---
    [Export] public float WallDuration = 2f;              // Vision 7: 2 s

    private CharacterBody3D _body = null!;
    private PlayerController _pc = null!;
    private Camera3D? _cam;
    private MeshInstance3D? _bashFlash, _warcryRing, _wallDisc;
    private float _bashCd, _warcryCd, _flashTimer;

    /// <summary>Remaining cooldowns for the HUD (ArenaHud.cs sweeps).</summary>
    public float BashCdRemaining => Mathf.Max(0f, _bashCd);
    public float WarcryCdRemaining => Mathf.Max(0f, _warcryCd);

    public IEnumerable<AbilitySlot> Slots()
    {
        yield return new AbilitySlot("E", "Bash",
            () => BashCdRemaining / BashCooldown, () => false);
        yield return new AbilitySlot("R", "Warcry",
            () => WarcryCdRemaining / WarcryCooldown, () => false);
        yield return new AbilitySlot("F", "Wall",
            () => 0f, () => _pc.IsShieldWalling);
    }

    public override void _Ready()
    {
        _body = (CharacterBody3D)GetParent();
        _pc = (PlayerController)_body;
        GD.Print("WARDEN KIT READY — E bash (cone stun), R warcry (+15%), F shield wall (2 s); hits validated server-side");
    }

    public override void _Process(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        _cam ??= GetViewport().GetCamera3D();
        if (_cam is null)
            return;

        if (_bashCd > 0f) _bashCd -= delta;
        if (_warcryCd > 0f) _warcryCd -= delta;

        if (Input.IsActionJustPressed("warden_bash"))
            ShieldBash();
        if (Input.IsActionJustPressed("warden_warcry"))
            Warcry();
        // No casting while down or authority-stunned (Vision 7 control rules).
        if (Input.IsActionJustPressed("warden_wall") && !_pc.IsShieldWalling
            && !_pc.IsDodging && !_pc.IsDead && !_pc.IsStunned)
            _pc.StartShieldWall(WallDuration);

        SyncWallDisc();
        SyncWarcryRing();

        if (_flashTimer > 0f)
        {
            _flashTimer -= delta;
            if (_flashTimer <= 0f && _bashFlash is not null)
                _bashFlash.Visible = false;
        }
    }

    // ------------------------------- Shield bash --------------------------

    private void ShieldBash()
    {
        if (_bashCd > 0f || _pc.IsDodging || _pc.IsDead || _pc.IsStunned ||
            !_pc.TrySpendStamina(BashStaminaCost))
            return;
        _bashCd = BashCooldown;

        Vector3 aim = Aim.CursorGroundPoint(_cam!);
        var toAim = aim - _body.GlobalPosition;
        toAim.Y = 0f;
        if (toAim.LengthSquared() < 0.0001f)
            toAim = -_body.GlobalBasis.Z;             // aim on self: bash forward
        Vector3 facing = toAim.Normalized();

        // Face the bash so the silhouette reads the strike (Vision 6.8).
        _body.RotationDegrees = new Vector3(0f,
            Mathf.RadToDeg(Mathf.Atan2(-facing.X, -facing.Z)), 0f);

        ShowBashCone(facing);
        // Prediction filter only — the server re-validates every request and
        // computes the damage/stun (CombatTables: ShieldBash). Candidates are
        // ALL combat targets: dummies and OTHER wardens (PvP, Vision 9).
        var authority = CombatAuthority.For(this);
        int requests = 0;
        foreach (var node in GetTree().GetNodesInGroup("combat_targets"))
        {
            if (node is not Node3D target || target == _body)
                continue;
            var to = target.GlobalPosition - _body.GlobalPosition;
            to.Y = 0f;
            if (to.Length() > BashRadius + 0.35f)     // + victim half-width
                continue;
            if (Mathf.RadToDeg(facing.AngleTo(to.Normalized())) > BashArcDegrees * 0.5f)
                continue;
            if (node is ICombatTarget combatTarget)
                authority?.RequestHit(combatTarget.CombatId, (int)AttackId.ShieldBash,
                    _body.GlobalPosition, facing);
            requests++;
        }
        GD.Print($"WARDEN BASH stun={BashStunSeconds:0.0}s requests={requests} cd={BashCooldown:0.0}s (server computes damage)");
    }

    /// <summary>Ground-projected cone flash — the visible bash hitbox.</summary>
    private void ShowBashCone(Vector3 facing)
    {
        if (_bashFlash is null)
        {
            _bashFlash = new MeshInstance3D
            {
                Name = "BashFlash",
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.85f, 0.81f, 0.75f, 0.4f),  // bone flash
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_bashFlash);
        }
        _bashFlash.Mesh = GroundShapes.Sector(BashRadius, BashArcDegrees, facing);
        _bashFlash.GlobalPosition = new Vector3(_body.GlobalPosition.X, 0.03f, _body.GlobalPosition.Z);
        _bashFlash.Visible = true;
        _flashTimer = 0.15f;
    }

    // -------------------------------- Warcry ------------------------------

    private void Warcry()
    {
        if (_warcryCd > 0f || _pc.IsDodging || _pc.IsDead || _pc.IsStunned)
            return;
        _warcryCd = WarcryCooldown;
        _pc.StartWarcry(WarcryDuration, WarcryBuffMultiplier);   // local mirror/VFX
        // The server owns buffs (sane-capped) — damage math happens there.
        CombatAuthority.For(this)?.RequestBuff(WarcryBuffMultiplier);
        GD.Print($"WARDEN WARCRY +{(WarcryBuffMultiplier - 1f) * 100f:0}% for {WarcryDuration:0}s cd={WarcryCooldown:0}s");
    }

    /// <summary>Buff radius reads as an accent ring on the floor while active
    /// (Vision 6.9: telegraphs/buffs live on the ground plane).</summary>
    private void SyncWarcryRing()
    {
        bool active = _pc.DamageMultiplier > 1f;
        if (active && _warcryRing is null)
        {
            _warcryRing = new MeshInstance3D
            {
                Name = "WarcryRing",
                Mesh = new TorusMesh { InnerRadius = WarcryRadius - 0.15f, OuterRadius = WarcryRadius + 0.15f },
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.69f, 0.55f, 0.34f, 0.45f),   // accent
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0f, 0.04f, 0f),
            };
            _body.AddChild(_warcryRing);
        }
        if (_warcryRing is not null)
        {
            _warcryRing.Visible = active;
            if (active)
                _warcryRing.RotationDegrees = new Vector3(0f,
                    Mathf.RadToDeg(Time.GetTicksMsec() * 0.02f % 360f), 0f);  // slow drift = alive
        }
    }

    // ------------------------------ Shield wall ---------------------------

    /// <summary>Blocking reads as a cold-steel disc at the feet while active.</summary>
    private void SyncWallDisc()
    {
        bool active = _pc.IsShieldWalling;
        if (active && _wallDisc is null)
        {
            _wallDisc = new MeshInstance3D
            {
                Name = "WallDisc",
                Mesh = new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.1f, Height = 0.02f },
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.77f, 0.8f, 0.86f, 0.5f),     // cold steel
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0f, 0.05f, 0f),
            };
            _body.AddChild(_wallDisc);
        }
        if (_wallDisc is not null)
            _wallDisc.Visible = active;
    }

    /// <summary>Rich state for the playtester runtime digest (mcp_watch).</summary>
    public Godot.Collections.Dictionary _mcp_state() => new()
    {
        ["bash_cd"] = Mathf.Max(0f, _bashCd),
        ["warcry_cd"] = Mathf.Max(0f, _warcryCd),
        ["buff_multiplier"] = _pc.DamageMultiplier,
        ["wall_active"] = _pc.IsShieldWalling,
    };
}
