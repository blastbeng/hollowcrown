using Godot;
using Hollowcrown.Combat;
using Hollowcrown.Player;

namespace Hollowcrown.World;

/// <summary>
/// Minimal test arena for the isometric camera rig (Vision 6.1 + 6.2):
/// floor, one gothic occluder wall, player stand-in, cold dusk light, ember
/// torch, fog/SSAO/glow environment, cursor reticle, occlusion fade.
/// Grows into the duel arena (Vision 6.6) in the next iteration.
/// </summary>
public partial class ArenaTest : Node3D
{
    private Node3D _player = null!;

    public override void _Ready()
    {
        BuildEnvironment();
        BuildFloor();
        BuildWall();
        BuildRingWall();
        BuildObelisk();
        BuildBrazier(-3.5f, 2.2f, -3.5f);   // spawn side
        BuildBrazier(3.5f, 2.2f, 3.5f);     // obelisk side
        BuildRubble();
        BuildDummy();
        BuildPlayer();
        BuildCameraAndHelpers();
        GD.Print("ARENA TEST READY — iso camera rig, cursor aim reticle, occlusion fade, combat live");
    }

    private void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = Color.FromHtml("23262e"),
            SkyHorizonColor = Color.FromHtml("191a20"),
            GroundBottomColor = Color.FromHtml("0e0f13"),
            GroundHorizonColor = Color.FromHtml("15161c"),
        };
        var env = new Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Color.FromHtml("1a1a22"),
            AmbientLightEnergy = 1.6f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.15f,
            FogEnabled = true,
            FogLightColor = Color.FromHtml("0e0f13"),
            FogDensity = 0.004f,
            VolumetricFogEnabled = true,
            VolumetricFogDensity = 0.008f,
            SsaoEnabled = true,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        // Cold low-energy dusk light with long dramatic shadows (Vision 6.1/6.2).
        var sun = new DirectionalLight3D
        {
            LightColor = Color.FromHtml("9aa7c0"),
            LightEnergy = 1.35f,
            ShadowEnabled = true,
        };
        sun.RotationDegrees = new Vector3(-55f, 30f, 0f);
        AddChild(sun);
    }

    /// <summary>Broken ring wall around the duel ground (Vision 6.6): 10
    /// segments on a 14 m circle, two knocked out as the "breach".</summary>
    private void BuildRingWall()
    {
        for (int i = 0; i < 10; i++)
        {
            if (i == 3 || i == 7)
                continue;  // the breach

            float angle = Mathf.Tau * i / 10f;
            var segment = new StaticBody3D { Name = $"RingWall{i}" };
            segment.AddToGroup("occluder");
            float height = (i == 5) ? 2.5f : 4f;  // one crumbled half-height piece
            segment.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(7.5f, height, 0.7f) },
                MaterialOverride = MaterialFactory.WallStone(),
                Position = new Vector3(0, height / 2f, 0),
            });
            segment.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(7.5f, height, 0.7f) },
                Position = new Vector3(0, height / 2f, 0),
            });
            segment.Position = new Vector3(Mathf.Sin(angle) * 14f, 0, Mathf.Cos(angle) * 14f);
            segment.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(angle) + 90f, 0f);
            AddChild(segment);
        }
    }

    /// <summary>Central obelisk: dark tapered stone, the arena's landmark.</summary>
    private void BuildObelisk()
    {
        var obelisk = new StaticBody3D { Name = "Obelisk" };
        obelisk.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.25f, BottomRadius = 0.6f, Height = 5f,
                RadialSegments = 4,
            },
            MaterialOverride = MaterialFactory.DarkStone(),
            Position = new Vector3(0, 2.5f, 0),
        });
        obelisk.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.9f, 5f, 0.9f) },
            Position = new Vector3(0, 2.5f, 0),
        });
        obelisk.Position = new Vector3(6f, 0, -6f);
        AddChild(obelisk);
    }

    /// <summary>Brazier: iron bowl on a dark-wood post with an ember light.</summary>
    private void BuildBrazier(float x, float y, float z)
    {
        var brazier = new Node3D { Name = "Brazier" };
        brazier.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.3f, Height = 0.4f },
            MaterialOverride = MaterialFactory.PlayerSteel(),
            Position = new Vector3(0, 1.15f, 0),
        });
        brazier.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.22f, 1f, 0.22f) },
            MaterialOverride = MaterialFactory.DarkWood(),
            Position = new Vector3(0, 0.5f, 0),
        });
        brazier.AddChild(new OmniLight3D
        {
            LightColor = Color.FromHtml("e08a3c"),       // ember (Vision 6.10)
            LightEnergy = 2.4f,
            OmniRange = 9f,
            ShadowEnabled = true,
            Position = new Vector3(0, 1.6f, 0),
        });
        brazier.Position = new Vector3(x, 0, z);
        AddChild(brazier);
    }

    /// <summary>Rubble piles: seeded deterministic MultiMesh stones (Vision 6.7/6.13).</summary>
    private void BuildRubble()
    {
        var rng = new RandomNumberGenerator { Seed = 1337 };  // deterministic (Vision 6)
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.35f, 0.4f) },
            InstanceCount = 36,
        };
        for (int i = 0; i < 36; i++)
        {
            // Four piles: near the breach segments and by the obelisk.
            float pileAngle = Mathf.Tau * (0.3f + 0.4f * (i % 2)) / 10f
                              + (float)rng.RandfRange(-0.15f, 0.15f);
            float pileDist = 12.5f + (float)rng.RandfRange(-1f, 1f);
            var t = Transform3D.Identity;
            t = t.Rotated(new Vector3(0, 1, 0), rng.RandfRange(0f, Mathf.Tau));
            t = t.Scaled(new Vector3(1, 1, 1) * (float)rng.RandfRange(0.5f, 1.4f));
            t.Origin = new Vector3(
                Mathf.Sin(pileAngle) * pileDist + (i >= 18 ? 6f : 0f),
                0.15f,
                Mathf.Cos(pileAngle) * pileDist + (i >= 18 ? -6f : 0f));
            multi.SetInstanceTransform(i, t);
        }
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = multi,
            MaterialOverride = MaterialFactory.RubbleStone(),
        });
    }

    private void BuildFloor()
    {
        var floor = new StaticBody3D { Name = "Floor" };
        floor.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(40, 40) },
            MaterialOverride = MaterialFactory.FloorStone(),
        });
        floor.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(40, 0.2f, 40) },
            Position = new Vector3(0, -0.1f, 0),
        });
        AddChild(floor);
    }

    private void BuildWall()
    {
        var wall = new StaticBody3D { Name = "Wall" };
        wall.AddToGroup("occluder");
        wall.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(8, 4, 0.6f) },  // 4 m gothic wall
            MaterialOverride = MaterialFactory.WallStone(),
            Position = new Vector3(0, 2, 0),
        });
        wall.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(8, 4, 0.6f) },
            Position = new Vector3(0, 2, 0),  // collider must match the raised mesh
        });
        // On the camera->player diagonal, just behind the stand-in: with the
        // fixed iso rig (pitch -50, offset 0,18,18) only geometry within ~3 m
        // of the target can actually block the view ray — perfect occluder test.
        wall.Position = new Vector3(1.5f, 0, -0.5f);
        wall.RotationDegrees = new Vector3(0f, 45f, 0f);
        AddChild(wall);
    }

    private void BuildDummy()
    {
        // Combat target 4 m up-screen of the spawn: inside the chain reach
        // after two steps of W, visible in the default iso framing.
        var dummy = new TrainingDummy { Name = "TrainingDummy" };
        dummy.Position = new Vector3(2.5f, 0f, -6f);
        AddChild(dummy);
    }

    private void BuildPlayer()
    {
        // Player controller owns the 1.8 m capsule visual now (Vision 6.4):
        // WASD camera-relative, sprint, dodge roll. Camera + occluder
        // TargetPaths keep pointing at this same node.
        _player = new PlayerController { Name = "Player" };
        _player.Position = new Vector3(0, 0.2f, -2);
        _player.AddChild(new WardenChain { Name = "WardenChain" });   // Vision 7 slice: Q/LMB sword chain
        AddChild(_player);
    }

    private void BuildCameraAndHelpers()
    {
        AddChild(new IsoCameraRig { Name = "IsoCameraRig", TargetPath = _player.GetPath() });
        AddChild(new Reticle { Name = "Reticle" });
        AddChild(new OcclusionFader { Name = "OcclusionFader", TargetPath = _player.GetPath() });
    }
}
