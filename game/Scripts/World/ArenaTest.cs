using Godot;
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
        BuildPlayer();
        BuildCameraAndHelpers();
        GD.Print("ARENA TEST READY — iso camera rig, cursor aim reticle, occlusion fade live");
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
            AmbientLightEnergy = 1.1f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
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
            LightEnergy = 1.0f,
            ShadowEnabled = true,
        };
        sun.RotationDegrees = new Vector3(-55f, 30f, 0f);
        AddChild(sun);

        // Torch light near the player (ember palette); flicker comes with the
        // atmosphere pass (Vision 6.2 noise flicker).
        AddChild(new OmniLight3D
        {
            LightColor = Color.FromHtml("e08a3c"),
            LightEnergy = 2.4f,
            OmniRange = 9f,
            ShadowEnabled = true,
            Position = new Vector3(3.5f, 2.2f, 3.5f),
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

    private void BuildPlayer()
    {
        _player = new Node3D { Name = "PlayerStandIn" };
        _player.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f },  // 1.8 m character
            MaterialOverride = MaterialFactory.PlayerSteel(),
            Position = new Vector3(0, 0.9f, 0),
        });
        _player.Position = new Vector3(0, 0, -2);
        AddChild(_player);
    }

    private void BuildCameraAndHelpers()
    {
        AddChild(new IsoCameraRig { Name = "IsoCameraRig", TargetPath = _player.GetPath() });
        AddChild(new Reticle { Name = "Reticle" });
        AddChild(new OcclusionFader { Name = "OcclusionFader", TargetPath = _player.GetPath() });
    }
}
