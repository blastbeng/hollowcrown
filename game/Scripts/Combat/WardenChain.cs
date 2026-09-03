using Godot;
using Hollowcrown.Core;

namespace Hollowcrown.Combat;

/// <summary>
/// Warden 3-hit sword arc chain (Vision 7): Q / LMB, each press advances the
/// combo while the window is open; the finisher is heavier. The hitbox is a
/// GROUND-PROJECTED arc sector (radius x 120 deg) anchored on the player and
/// aimed at the cursor ground point — the flash mesh IS the hitbox, drawn flat
/// on the floor (Vision 6.9). Client-side slice: the match server will compute
/// these hits once combat goes online (Vision 2.3); shapes and numbers live
/// here and in BALANCE.md as the single source.
/// </summary>
public partial class WardenChain : Node3D
{
    [Signal] public delegate void ChainSwingEventHandler(int index, Vector3 aimPoint);

    [Export] public float Reach = 2.4f;           // BALANCE.md: warden_chain
    [Export] public float ArcDegrees = 120f;
    [Export] public float ComboWindow = 0.9f;

    private static readonly int[] Damage = { 20, 20, 35 };   // finisher heavier

    private CharacterBody3D _body = null!;
    private Camera3D? _cam;
    private MeshInstance3D? _arcFlash;
    private int _combo;
    private float _comboTimer, _flashTimer;

    public override void _Ready()
    {
        _body = (CharacterBody3D)GetParent();
        GD.Print("WARDEN CHAIN READY — Q/LMB: 3-hit sword arc (20/20/35), 120deg x 2.4m ground-projected hitbox");
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
                _combo = 0;                        // window closed: chain resets
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
        Vector3 aim = Aim.CursorGroundPoint(_cam!);
        var toAim = aim - _body.GlobalPosition;
        toAim.Y = 0f;
        if (toAim.LengthSquared() < 0.0001f)
            toAim = -_body.GlobalBasis.Z;          // aim on self: swing forward
        Vector3 facing = toAim.Normalized();

        // Face the swing so the silhouette reads the attack (Vision 6.8).
        _body.RotationDegrees = new Vector3(0f,
            Mathf.RadToDeg(Mathf.Atan2(-facing.X, -facing.Z)), 0f);

        int index = _combo;
        _combo = (_combo + 1) % Damage.Length;
        _comboTimer = ComboWindow;

        ShowArc(facing);
        bool heavy = index == Damage.Length - 1;
        int hits = 0;
        foreach (var node in GetTree().GetNodesInGroup("dummies"))
        {
            if (node is not TrainingDummy dummy)
                continue;
            var to = dummy.GlobalPosition - _body.GlobalPosition;
            to.Y = 0f;
            float dist = to.Length();
            if (dist > Reach + 0.35f)              // + dummy half-width
                continue;
            float angle = Mathf.RadToDeg(facing.AngleTo(to.Normalized()));
            if (angle > ArcDegrees * 0.5f)
                continue;
            if (dummy.TakeDamage(Damage[index], heavy))
                hits++;
        }
        EmitSignal(SignalName.ChainSwing, index, aim);
        GD.Print($"WARDEN SWING {index + 1}/3 dmg={Damage[index]} hits={hits}");
    }

    /// <summary>Ground-projected arc sector flash — the visible hitbox.</summary>
    private void ShowArc(Vector3 facing)
    {
        if (_arcFlash is null)
        {
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.69f, 0.55f, 0.34f, 0.35f),  // accent, 35%
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            _arcFlash = new MeshInstance3D
            {
                Name = "ArcFlash",
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_arcFlash);
        }

        _arcFlash.Mesh = BuildSectorMesh(facing);
        _arcFlash.GlobalPosition = new Vector3(_body.GlobalPosition.X, 0.03f, _body.GlobalPosition.Z);
        _arcFlash.Visible = true;
        _flashTimer = 0.15f;
    }

    private ArrayMesh BuildSectorMesh(Vector3 facing)
    {
        const int segments = 14;
        float arcRad = Mathf.DegToRad(ArcDegrees);
        float half = arcRad * 0.5f;
        float baseYaw = Mathf.Atan2(-facing.X, -facing.Z);     // body-style yaw
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 origin = new Vector3(0, 0, 0);
        for (int i = 0; i < segments; i++)
        {
            float a0 = -half + arcRad * i / segments;
            float a1 = -half + arcRad * (i + 1) / segments;
            // Sector in the XZ plane: angle measured around +Up from facing.
            var p0 = PointAt(baseYaw + a0);
            var p1 = PointAt(baseYaw + a1);
            st.SetNormal(Vector3.Up);
            st.AddVertex(origin);
            st.SetNormal(Vector3.Up);
            st.AddVertex(p0);
            st.SetNormal(Vector3.Up);
            st.AddVertex(p1);
        }
        var mesh = st.Commit();
        return mesh;
    }

    private Vector3 PointAt(float yaw)
    {
        // Forward = -Z rotated by yaw around Up (Godot convention).
        return new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw)) * Reach;
    }
}
