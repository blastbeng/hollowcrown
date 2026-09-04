using System.Collections.Generic;
using Godot;
using Hollowcrown.Core;
using Hollowcrown.Networking;
using Hollowcrown.Player;

namespace Hollowcrown.Combat;

/// <summary>
/// Nightblade kit (Vision 7): shadow step (E — 6 m blink toward the cursor),
/// stealth (R — 5 s, breaks on attack, next hit +50%), smoke bomb (F — blind
/// zone at the cursor). Movement (blink) is client-simulated like WASD —
/// trust checks on position reports are a separate hardening task — while
/// the STEALTH BONUS and the SMOKE ZONE are server-owned: the authority
/// tracks stealth state (breaks it on the next hit, +50% that hit) and
/// broadcasts smoke zones that REJECT hits blinded by the cloud.
/// </summary>
public partial class NightbladeKit : Node3D, IAbilityProvider
{
    // --- Shadow step (BALANCE.md: nightblade_step) ---
    [Export] public float StepDistance = 6f;
    [Export] public float StepStaminaCost = 15f;
    [Export] public float StepCooldown = 8f;

    // --- Smoke bomb (BALANCE.md: nightblade_smoke) ---
    [Export] public float SmokeStaminaCost = 25f;
    [Export] public float SmokeCooldown = CombatTables.SmokeCooldown;

    private PlayerController _pc = null!;
    private Camera3D? _cam;
    private MeshInstance3D? _stepFlash;
    private float _stepCd, _smokeCd, _flashTimer;

    public float StepCdRemaining => Mathf.Max(0f, _stepCd);
    public float SmokeCdRemaining => Mathf.Max(0f, _smokeCd);

    public override void _Ready()
    {
        _pc = (PlayerController)GetParent();
        GD.Print("NIGHTBLADE KIT READY — E shadow step (6m blink), R stealth (+50% next hit), F smoke bomb (blind zone)");
    }

    public IEnumerable<AbilitySlot> Slots()
    {
        yield return new AbilitySlot("E", "Step",
            () => StepCdRemaining / StepCooldown, () => false);
        yield return new AbilitySlot("R", "Stealth",
            () => 0f, () => _pc.IsStealthed);
        yield return new AbilitySlot("F", "Smoke",
            () => SmokeCdRemaining / SmokeCooldown, () => false);
    }

    public override void _Process(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        _cam ??= GetViewport().GetCamera3D();
        if (_cam is null)
            return;

        if (_stepCd > 0f) _stepCd -= delta;
        if (_smokeCd > 0f) _smokeCd -= delta;

        if (Input.IsActionJustPressed("nb_step"))
            ShadowStep();
        if (Input.IsActionJustPressed("nb_stealth"))
            Stealth();
        if (Input.IsActionJustPressed("nb_smoke"))
            SmokeBomb();

        if (_flashTimer > 0f)
        {
            _flashTimer -= delta;
            if (_flashTimer <= 0f && _stepFlash is not null)
                _stepFlash.Visible = false;
        }
    }

    // ------------------------------ Shadow step ---------------------------

    private void ShadowStep()
    {
        if (_stepCd > 0f || _pc.IsDodging || _pc.IsDead || _pc.IsStunned ||
            !_pc.TrySpendStamina(StepStaminaCost))
            return;
        _stepCd = StepCooldown;

        Vector3 aim = Aim.CursorGroundPoint(_cam!);
        var toAim = aim - _pc.GlobalPosition;
        toAim.Y = 0f;
        if (toAim.LengthSquared() < 0.0001f)
            toAim = -_pc.GlobalBasis.Z;
        Vector3 dir = toAim.Normalized();

        // Never blink inside a wall: ray along the blink path, shorten on hit.
        float dist = StepDistance;
        var space = _pc.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(
            _pc.GlobalPosition + new Vector3(0, 1.0f, 0),
            _pc.GlobalPosition + new Vector3(0, 1.0f, 0) + dir * StepDistance);
        query.Exclude = new Godot.Collections.Array<Rid> { _pc.GetRid() };
        var hit = space.IntersectRay(query);
        if (hit.Count > 0)
        {
            var hitPos = (Vector3)hit["position"];
            dist = Mathf.Max(0.8f, (hitPos - _pc.GlobalPosition).Length() - 0.6f);
        }

        ShowStepFlash(dir, dist);
        _pc.GlobalPosition += dir * dist;          // the blink
        _pc.Velocity = Vector3.Zero;
        GD.Print($"NIGHTBLADE STEP {dist:0.0}m toward cursor");
    }

    /// <summary>Dark smoke streak along the blink path (Vision 6.9 trail).</summary>
    private void ShowStepFlash(Vector3 dir, float dist)
    {
        if (_stepFlash is null)
        {
            _stepFlash = new MeshInstance3D
            {
                Name = "StepFlash",
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.42f, 0.29f, 0.54f, 0.45f),   // arcane-tinted shadow
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_stepFlash);
        }
        _stepFlash.Mesh = GroundShapes.Line(dist, 1.2f, dir);
        _stepFlash.GlobalPosition = new Vector3(_pc.GlobalPosition.X, 0.03f, _pc.GlobalPosition.Z);
        _stepFlash.Visible = true;
        _flashTimer = 0.22f;
    }

    // -------------------------------- Stealth ------------------------------

    private void Stealth()
    {
        if (_pc.IsDodging || _pc.IsDead || _pc.IsStunned || _pc.IsStealthed)
            return;
        // Server owns the state (cooldown, duration, break-on-attack, +50%).
        CombatAuthority.For(this)?.RequestStealth();
        GD.Print("NIGHTBLADE STEALTH requested — server grants/breaks the state");
    }

    // ------------------------------ Smoke bomb -----------------------------

    private void SmokeBomb()
    {
        if (_smokeCd > 0f || _pc.IsDodging || _pc.IsDead || _pc.IsStunned ||
            !_pc.TrySpendStamina(SmokeStaminaCost))
            return;
        _smokeCd = SmokeCooldown;

        Vector3 aim = Aim.CursorGroundPoint(_cam!);
        var toAim = aim - _pc.GlobalPosition;
        toAim.Y = 0f;
        float dist = Mathf.Min(toAim.Length(), CombatTables.SmokeThrowRange);
        Vector3 pos = dist > 0.001f
            ? _pc.GlobalPosition + toAim.Normalized() * dist
            : _pc.GlobalPosition;
        CombatAuthority.For(this)?.RequestSmoke(pos);
        GD.Print($"NIGHTBLADE SMOKE at {pos} (server validates range + cooldown, broadcasts the zone)");
    }

    /// <summary>Rich state for the playtester runtime digest (mcp_watch).</summary>
    public Godot.Collections.Dictionary _mcp_state() => new()
    {
        ["step_cd"] = Mathf.Max(0f, _stepCd),
        ["smoke_cd"] = Mathf.Max(0f, _smokeCd),
        ["stealthed"] = _pc.IsStealthed,
    };
}
