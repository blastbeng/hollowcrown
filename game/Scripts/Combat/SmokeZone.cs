using Godot;

namespace Hollowcrown.Combat;

/// <summary>
/// Nightblade smoke bomb zone (Vision 7): a blind cloud thrown to the cursor
/// ground point. The CombatAuthority BROADCASTS the zone (identical spawn on
/// every peer), so this node doubles as shared world state: hits through or
/// out of the smoke are rejected server-side (blinded), and any LOCAL player
/// standing inside gets a screen-darkening blind overlay (PlayerController).
/// Self-expiring after CombatTables.SmokeDuration.
/// </summary>
public partial class SmokeZone : Node3D
{
    public float Radius { get; set; } = CombatTables.SmokeRadius;
    public double Until { get; set; }

    private GpuParticles3D _puffs = null!;

    public override void _Ready()
    {
        AddToGroup("smoke_zone");
        Until = Time.GetTicksMsec() / 1000.0 + CombatTables.SmokeDuration;

        // Dark ground disc: marks the zone's true radius flat on the floor.
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = Radius, BottomRadius = Radius, Height = 0.02f,
                RadialSegments = 24,
            },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.05f, 0.05f, 0.07f, 0.55f),
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            Position = new Vector3(0f, 0.04f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // Rising smoke puffs (Vision 6.7: fog drift near the ground).
        var mat = new ParticleProcessMaterial
        {
            Direction = new Vector3(0, 1, 0),
            Spread = 25f,
            InitialVelocityMin = 0.6f,
            InitialVelocityMax = 1.4f,
            Gravity = new Vector3(0, 0.25f, 0),
            ScaleMin = 1.6f,
            ScaleMax = 3.2f,
        };
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(0.10f, 0.10f, 0.12f, 0.85f));
        ramp.SetColor(1, new Color(0.08f, 0.08f, 0.10f, 0.0f));
        var rampTex = new GradientTexture1D { Gradient = ramp };
        mat.ColorRamp = rampTex;
        _puffs = new GpuParticles3D
        {
            Amount = 90,
            Lifetime = 1.6f,
            ProcessMaterial = mat,
            DrawPass1 = new SphereMesh { Radius = 0.35f, Height = 0.7f, RadialSegments = 8, Rings = 4 },
            VisibilityAabb = new Aabb(new Vector3(-6, -1, -6), new Vector3(12, 8, 12)),
        };
        var emitBox = new BoxShape3D { Size = new Vector3(Radius * 1.5f, 0.4f, Radius * 1.5f) };
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        mat.EmissionBoxExtents = emitBox.Size * 0.5f;
        _puffs.MaterialOverride = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(_puffs);
    }

    public bool Contains(Vector3 point)
    {
        var d = point - GlobalPosition;
        d.Y = 0f;
        return d.Length() <= Radius;
    }

    public static bool AnyZoneContains(Node treeRoot, Vector3 point)
    {
        foreach (var node in treeRoot.GetTree().GetNodesInGroup("smoke_zone"))
            if (node is SmokeZone zone && zone.Contains(point))
                return true;
        return false;
    }

    public override void _Process(double _delta)
    {
        if (Time.GetTicksMsec() / 1000.0 > Until)
            QueueFree();       // zone expires identically on every peer
    }
}
