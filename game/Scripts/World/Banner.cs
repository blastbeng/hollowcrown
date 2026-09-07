using Godot;

namespace Hollowcrown.World;

/// <summary>
/// Cheap world-seller details (Vision 6.7): waving war banners, hanging
/// chains, cobwebs. All static-cached primitives + palette materials —
/// banner sway is a per-frame sin() on a rotated pivot (no skeleton, no
/// shader compile risk), chains are dark-steel segment quads, cobwebs are
/// translucent corner quads. Blender MCP unavailable — noted in the commit.
/// </summary>
public static partial class Banner
{
    /// <summary>War banner on a pole: dark cloth with an accent chevron,
    /// sways around the pole top. Attach at ground position.</summary>
    public static Node3D WarBanner(Color cloth, float height = 3.2f)
    {
        var root = new Node3D { Name = "WarBanner" };

        // Pole: dark wood, slight taper via two stacked boxes.
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.09f, height, 0.09f) },
            MaterialOverride = MaterialFactory.DarkWood(),
            Position = new Vector3(0, height / 2f, 0),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.1f, 0.14f) },
            MaterialOverride = MaterialFactory.WeaponSteel(),   // iron cap
            Position = new Vector3(0, height + 0.05f, 0),
        });

        // Cloth pivot AT THE POLE TOP: rotation here reads as wind sway.
        var clothPivot = new Node3D { Name = "ClothPivot" };
        clothPivot.Position = new Vector3(0, height - 0.12f, 0);

        var clothMat = new StandardMaterial3D
        {
            AlbedoColor = cloth,
            Roughness = 0.95f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var accentMat = new StandardMaterial3D
        {
            AlbedoColor = Color.FromHtml("b08d57"),   // UI accent gold (Vision 6.10)
            Roughness = 0.85f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        clothPivot.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.9f, 1.5f, 0.03f) },
            MaterialOverride = clothMat,
            Position = new Vector3(0.45f, -0.5f, 0),
        });
        clothPivot.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.16f, 0.035f) },
            MaterialOverride = accentMat,
            Position = new Vector3(0.45f, -0.95f, 0),
        });
        root.AddChild(clothPivot);

        var sway = new SwayDriver { Pivot = clothPivot, Phase = (float)GD.RandRange(0.0, 6.28) };
        root.AddChild(sway);
        return root;
    }

    /// <summary>Hanging chain: a few dark steel links dropping from the
    /// anchor point (attach AT the ceiling/arch top).</summary>
    public static Node3D Chain(float length = 1.2f)
    {
        var chain = new Node3D { Name = "Chain" };
        int links = Mathf.Max(2, (int)(length / 0.18f));
        for (int i = 0; i < links; i++)
        {
            chain.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh
                {
                    InnerRadius = 0.035f,
                    OuterRadius = 0.07f,
                },
                MaterialOverride = MaterialFactory.WeaponSteel(),
                Position = new Vector3(0, -0.09f - i * 0.18f, 0),
                RotationDegrees = new Vector3(0, 0, (i % 2) * 90f),
            });
        }
        return chain;
    }

    /// <summary>Cobweb: translucent quad draped in a corner (alpha 0.12,
    /// unshaded). Rotate to face the corner diagonal.</summary>
    public static MeshInstance3D Cobweb(float size = 0.9f)
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.85f, 0.85f, 0.9f, 0.12f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DisableReceiveShadows = true,
        };
        return new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            MaterialOverride = mat,
        };
    }

    /// <summary>Drives the banner sway: gentle sin() yaw + roll on the cloth
    /// pivot (wind gust = sum of two frequencies, deterministic per phase).</summary>
    private partial class SwayDriver : Node
    {
        public Node3D Pivot = null!;
        public float Phase;
        private float _t;

        public override void _Process(double delta)
        {
            _t += (float)delta;
            float yaw = Mathf.Sin(_t * 1.1f + Phase) * 7f;
            float roll = Mathf.Sin(_t * 1.7f + Phase * 0.6f) * 4f
                         + Mathf.Sin(_t * 0.4f) * 2f;
            Pivot.RotationDegrees = new Vector3(0, yaw, roll);
        }
    }
}
