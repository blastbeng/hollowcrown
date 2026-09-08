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
        BuildArches();
        BuildBrazier(-3.5f, 2.2f, -3.5f);   // spawn side
        BuildBrazier(3.5f, 2.2f, 3.5f);     // obelisk side
        BuildRubble();
        BuildBannersAndDetails();
        BuildGraveyard();
        BuildDummy();
        BuildPlayer();
        BuildCameraAndHelpers();
        GD.Print("ARENA TEST READY — iso camera rig, cursor aim reticle, occlusion fade, combat live, gothic arches + banners, dusk sky + graveyard props");
    }

    private void BuildEnvironment()
    {
        // --- DUSK SKY (EmacEArt Cemetery Pack, graveyard polish) ---
        // Stylized procedural sky replaces the flat ProceduralSkyMaterial.
        // Parameters are the exact set from Materials/EA_Skybox_Dusk.tres
        // (hardcoded here so the build-in-code architecture stays intact).
        // The shader syncs its sun disc/glow to this node's DirectionalLight3D
        // via LIGHT0_DIRECTION (sun_follows_light = true).
        var skyMat = new ShaderMaterial { Shader = GD.Load<Shader>(
            "res://assets/models/cemetery/Shaders/EA_Skybox.gdshader") };
        skyMat.SetShaderParameter("zenith_color", new Color(0.07f, 0.06f, 0.22f));
        skyMat.SetShaderParameter("horizon_color", new Color(0.46f, 0.3f, 0.4f));
        skyMat.SetShaderParameter("ground_color", new Color(0.17f, 0.17f, 0.22f));
        skyMat.SetShaderParameter("horizon_sharpness", 3.6f);
        skyMat.SetShaderParameter("horizon_offset", 0.05f);
        skyMat.SetShaderParameter("sun_follows_light", true);
        skyMat.SetShaderParameter("sun_direction_manual", new Vector3(-0.5859f, 0.723f, 0.3661f));
        skyMat.SetShaderParameter("sun_color", new Color(1f, 0.88f, 0.7f));
        skyMat.SetShaderParameter("sun_glow_color", new Color(0.85f, 0.6f, 0.45f));
        skyMat.SetShaderParameter("sun_size", 0.012f);
        skyMat.SetShaderParameter("sun_glow_size", 0.4f);
        skyMat.SetShaderParameter("sun_glow_falloff", 7f);
        skyMat.SetShaderParameter("sky_gradient_color", new Color(1f, 0.44f, 0.14f));
        skyMat.SetShaderParameter("sky_gradient_angle", 235f);
        skyMat.SetShaderParameter("sky_gradient_spread", 0.72f);
        skyMat.SetShaderParameter("sky_gradient_strength", 0.26f);
        skyMat.SetShaderParameter("atmosphere_color", new Color(0.42f, 0.36f, 0.7f));
        skyMat.SetShaderParameter("atmosphere_strength", 0.6f);
        skyMat.SetShaderParameter("atmosphere_falloff", 4.2f);
        skyMat.SetShaderParameter("exposure", 0.9f);
        skyMat.SetShaderParameter("contrast", 1.02f);
        skyMat.SetShaderParameter("saturation", 0.98f);
        skyMat.SetShaderParameter("lift", new Color(0f, 0f, 0f));
        skyMat.SetShaderParameter("gamma_midtones", new Color(0.5f, 0.5f, 0.5f));
        skyMat.SetShaderParameter("gain", new Color(0.9836f, 0.9889f, 0.9624f));
        skyMat.SetShaderParameter("clouds_enabled", true);
        skyMat.SetShaderParameter("cloud_color", new Color(0.42f, 0.35f, 0.55f));
        skyMat.SetShaderParameter("cloud_shadow_color", new Color(0.13f, 0.11f, 0.26f));
        skyMat.SetShaderParameter("cloud_coverage", 0.62f);
        skyMat.SetShaderParameter("cloud_softness", 0.32f);
        skyMat.SetShaderParameter("cloud_bands", 4f);
        skyMat.SetShaderParameter("cloud_density", 0.5f);
        skyMat.SetShaderParameter("cloud_stretch", 0.36f);
        skyMat.SetShaderParameter("cloud_swirl", 0.364f);
        skyMat.SetShaderParameter("cloud_style", 0.276f);
        skyMat.SetShaderParameter("cloud_scale", 4.45f);
        skyMat.SetShaderParameter("cloud_height", 0f);
        skyMat.SetShaderParameter("cloud_speed", 0.336f);

        // Fog retuned to the dusk horizon tone; ambient now feeds from the
        // sky itself (the shader's own color grading does the rest).
        var env = new Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = skyMat },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 1.0f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.15f,
            FogEnabled = true,
            FogLightColor = Color.FromHtml("4a3040"),   // warm dark plum horizon
            FogDensity = 0.003f,
            VolumetricFogEnabled = true,
            VolumetricFogDensity = 0.006f,
            SsaoEnabled = true,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        // Warm amber dusk light, sun low (-28 deg elevation) so the shader's
        // glow hugs the horizon. The shader reads LIGHT0_DIRECTION directly.
        var sun = new DirectionalLight3D
        {
            LightColor = Color.FromHtml("c9a06a"),
            LightEnergy = 1.5f,
            ShadowEnabled = true,
        };
        sun.RotationDegrees = new Vector3(-28f, 30f, 0f);
        AddChild(sun);

        // --- ROLLBACK (previous flat ProceduralSky look) ---
        // var sky = new ProceduralSkyMaterial
        // {
        //     SkyTopColor = Color.FromHtml("23262e"),
        //     SkyHorizonColor = Color.FromHtml("191a20"),
        //     GroundBottomColor = Color.FromHtml("0e0f13"),
        //     GroundHorizonColor = Color.FromHtml("15161c"),
        // };
        // AmbientLightSource = Godot.Environment.AmbientSource.Color,
        // AmbientLightColor = Color.FromHtml("1a1a22"),
        // AmbientLightEnergy = 1.6f,
        // FogLightColor = Color.FromHtml("0e0f13"),
        // FogDensity = 0.004f, VolumetricFogDensity = 0.008f, GlowIntensity = 0.5f
        // sun: LightColor "9aa7c0", LightEnergy 1.35f, RotationDegrees (-55, 30, 0)
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
        var embers = AtmosphereParticles.Embers();       // Vision 6.2/6.7
        embers.Position = new Vector3(0, 1.45f, 0);
        brazier.AddChild(embers);
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
            // ~2x chunkier stones (Vision 6.7 note): 0.35 m pebbles read as
            // noise at iso zoom; 0.5-1.4 m blocks read as battle debris.
            t = t.Scaled(new Vector3(1, 1, 1) * (float)rng.RandfRange(1.0f, 2.8f));
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

    /// <summary>Gothic arches (Vision 6.6): Quaternius Medieval Village
    /// MegaKit Wall_Arch panels (CC0, ATTRIBUTION.md) flanking the arena's
    /// west/east sides — ogival stone openings that frame the fight, plus a
    /// broken half-arch pair by the breach. Collision matches the panel.
    /// Blender MCP unavailable — store kit pieces were used instead.</summary>
    private void BuildArches()
    {
        // Panel = 2 m wide x 3 m tall x 0.06 m thick (kit bbox measured).
        (float x, float z, float yaw, bool broken)[] placements =
        {
            (-9.5f, -4f, 90f, false),   // west pair, opening faces the ring
            (-9.5f, 4f, 90f, false),
            (9.5f, -4f, 90f, false),    // east pair
            (9.5f, 4f, 90f, true),      // one broken piece
        };
        foreach (var (x, z, yawDeg, broken) in placements)
        {
            var arch = new StaticBody3D { Name = "Arch" };
            arch.AddToGroup("occluder");
            // .gltf imports as a PackedScene (meshes + kit materials inside) —
            // loading it as Mesh throws in Godot 4.7 and killed the arena
            // build once already. Instantiate the scene node instead.
            var archScene = GD.Load<PackedScene>("res://assets/models/medieval_kit/Wall_Arch.gltf");
            if (archScene?.Instantiate() is Node archModel)
            {
                arch.AddChild(archModel);
                // The kit panel ships wood-trim materials — wrong for gothic
                // STONE. Re-skin every mesh with the arena's wall stone so
                // the arches read as part of the ruin (palette 6.10).
                RetintToStone(archModel);
            }
            // Panel collision: legs + lintel approximated by one 2 x 3 frame
            // box minus the door gap — two leg boxes keep the opening walkable.
            arch.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(0.5f, 3f, 0.2f) },
                Position = new Vector3(-0.75f, 1.5f, 0),
            });
            arch.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(0.5f, 3f, 0.2f) },
                Position = new Vector3(0.75f, 1.5f, 0),
            });
            arch.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(2f, 0.8f, 0.2f) },
                Position = new Vector3(0, 2.6f, 0),
            });
            arch.Position = new Vector3(x, 0, z);
            arch.RotationDegrees = new Vector3(0, yawDeg, 0);
            if (broken)
            {
                arch.RotationDegrees = new Vector3(0, yawDeg + 8f, 4f);  // sunk/tipped
                arch.Position = new Vector3(x, -0.15f, z);
            }
            AddChild(arch);
        }
    }

    /// <summary>Re-skin kit geometry to the arena palette: every
    /// MeshInstance3D under the instantiated .gltf gets the cached wall-stone
    /// material (Vision 6.3: nothing visible keeps a foreign material).</summary>
    private static void RetintToStone(Node root)
    {
        if (root is MeshInstance3D mi)
            mi.MaterialOverride = MaterialFactory.WallStone();
        foreach (var child in root.GetChildren())
            RetintToStone(child);
    }

    /// <summary>Vision 6.7 world sellers: war banners on poles by the breach,
    /// hanging chains under the arch lintels, cobwebs in the corners of the
    /// west arches (all Banner.cs, palette materials).</summary>
    private void BuildBannersAndDetails()
    {
        // Banners flank the breach on the ring wall (blood-red + warden steel).
        var banner1 = Banner.WarBanner(Color.FromHtml("7a1414"));   // blood
        banner1.Position = new Vector3(-1.5f, 0, 12.4f);
        banner1.RotationDegrees = new Vector3(0, 180f, 0);
        AddChild(banner1);
        var banner2 = Banner.WarBanner(Color.FromHtml("4a4f5a"));   // cold steel
        banner2.Position = new Vector3(1.5f, 0, 12.4f);
        banner2.RotationDegrees = new Vector3(0, 180f, 0);
        AddChild(banner2);

        // Chains under the intact arch lintels (dropping from y=2.9).
        foreach (var (x, z) in new[] { (-9.5f, -4f), (-9.5f, 4f), (9.5f, -4f) })
        {
            var chain = Banner.Chain(1.1f);
            chain.Position = new Vector3(x + 0.7f, 2.9f, z + 0.55f);
            AddChild(chain);
        }

        // Cobwebs in the arch corners — face the camera diagonal.
        foreach (var (x, z) in new[] { (-9.5f, -4f), (-9.5f, 4f), (9.5f, 4f) })
        {
            var web = Banner.Cobweb(1.1f);
            web.Position = new Vector3(x - 0.6f, 2.55f, z - 0.75f);
            web.RotationDegrees = new Vector3(90f, -45f, 0f);
            AddChild(web);
        }
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
        // Player controller owns the rigged class model (Vision 6.4/6.8):
        // WASD camera-relative, sprint, dodge roll, class kit. The class
        // comes from the boot flow (Main: character select / --class flag).
        _player = new PlayerController
        {
            Name = "Player",
            Class = PlayerController.PendingClass,
        };
        _player.Position = new Vector3(0, 0.2f, -2);
        AddChild(_player);
    }

    private void BuildCameraAndHelpers()
    {
        AddChild(new IsoCameraRig { Name = "IsoCameraRig", TargetPath = _player.GetPath() });
        AddChild(new Reticle { Name = "Reticle" });
        AddChild(new OcclusionFader { Name = "OcclusionFader", TargetPath = _player.GetPath() });
        AddChild(new UI.ArenaHud { Name = "ArenaHud", PlayerPath = _player.GetPath() });   // Vision 6.10

        var rain = AtmosphereParticles.Rain(new Vector2(38, 38));   // Vision 6.2: rain outdoors
        rain.Position = new Vector3(0, 14f, 0);
        AddChild(rain);
    }

    /// <summary>Graveyard dressing (Vision 6.6 ruined graveyard arena):
    /// EmacEArt Low Poly Cemetery Grave Pack glbs scattered inside the ring
    /// wall — tilted headstones for cover, an archway gate on the breach,
    /// lanterns by the obelisk, one dead tree. Deterministic seed so the
    /// layout is stable between runs; cover props get fitted colliders.
    /// HDRI (belfast_sunset_puresky_1k.hdr) is deliberately NOT loaded —
    /// the stylized sky shader replaces it; kept for lighting experiments.</summary>
    private void BuildGraveyard()
    {
        var rng = new RandomNumberGenerator { Seed = 1337 };  // deterministic (Vision 6)

        // (x, z, yawDeg, tiltDeg, scale) — spread around the ring at radius
        // 10-20, clear of both spawn lanes (±5, ∓8) and the very center.
        (float x, float z, float yaw, float tilt, float scale)[] spots =
        {
            (-11f, -9f, 20f, 6f, 1.1f),    // slabs NW quadrant
            (-16f, 4f, 80f, 4f, 0.95f),
            (-4f, 14f, 170f, 5f, 1.0f),
            (11f, 12f, 245f, 7f, 1.15f),
            (16f, -3f, 305f, 10f, 1.05f),
            (3f, -13f, 15f, 8f, 1.1f),
        };
        foreach (var (x, z, yaw, tilt, scale) in spots)
            AddGraveProp("Slab_Tall", x, z, yaw, tilt, scale, collide: true, rng);

        // Obelisk + pyramid mix, tucked between the slabs.
        AddGraveProp("Obelisk", -18f, 9f, 75f, 3f, 1.0f, collide: true, rng);
        AddGraveProp("Obelisk", 19f, 8f, 250f, 4f, 0.9f, collide: true, rng);
        AddGraveProp("Obelisk", 18f, -9f, 300f, 2f, 1.05f, collide: true, rng);
        AddGraveProp("Pyramid", -7f, 12.5f, 150f, 2f, 1.0f, collide: false, rng);
        AddGraveProp("Pyramid", 7f, 12f, 205f, 3f, 1.1f, collide: false, rng);
        AddGraveProp("Pyramid", -8f, -12f, 35f, 2f, 0.95f, collide: false, rng);
        AddGraveProp("Pyramid", 8f, -13f, 65f, 4f, 1.05f, collide: false, rng);

        // Ruined wooden gate on the east breach (ring gap at i==7, x≈14).
        AddGraveProp("Archway_Wood", 14.2f, 0f, 90f, 6f, 1.1f, collide: true, rng);

        // Railing row along the NW ring stretch, slightly inside the wall.
        for (int i = 0; i < 4; i++)
            AddGraveProp("Railing", -13f + i * 1.9f, -12.5f - i * 0.6f, 115f + i * 3f,
                2f, 1.0f, collide: true, rng);

        // Lanterns near the center features (obelisk side); no lights yet.
        AddGraveProp("Lantern_Stone", 7.6f, -4.4f, 210f, 1f, 1.0f, collide: false, rng);
        AddGraveProp("Lantern_Stone", 4.6f, -7.6f, 30f, 2f, 0.95f, collide: false, rng);

        // Clutter: mud stones / log pile / posts scattered low around ring.
        AddGraveProp("Mud_Stones", -6f, 9.5f, 95f, 1f, 1.2f, collide: false, rng);
        AddGraveProp("Mud_Stones", 12f, 8f, 160f, 1f, 1.0f, collide: false, rng);
        AddGraveProp("Mud_Stones", 6f, -10f, 20f, 1f, 0.9f, collide: false, rng);
        AddGraveProp("Mud_Post", -12f, 6f, 140f, 3f, 1.1f, collide: false, rng);
        AddGraveProp("Mud_Post", 13f, 2f, 250f, 2f, 1.0f, collide: false, rng);
        AddGraveProp("LogPile", -14.5f, 1f, 100f, 2f, 1.0f, collide: false, rng);

        // One dead tree by the west wall — big cover piece near a breach.
        AddGraveProp("Tree_3b", -19f, -6f, 60f, 2f, 1.0f, collide: true, rng);

        GD.Print("GRAVEYARD: props placed (spawn lanes + center kept clear)");
    }

    /// <summary>Instantiate one cemetery glb as a scene node. Cover props get
    /// a StaticBody3D + box collider fitted to the SCALED Aabb (offset must
    /// mirror the mesh offset from body origin — the half-buried-wall
    /// gotcha). Missing/unimported glbs are skipped with a warning.</summary>
    private void AddGraveProp(string shortName, float x, float z, float yawDeg,
        float tiltDeg, float scale, bool collide, RandomNumberGenerator rng)
    {
        string path = $"res://assets/models/cemetery/Meshes/EA_{shortName switch
        {
            "Tree_3b" => "Environment_Nature_Tree_3b",
            _ => $"Grave_{shortName}",
        }}_01a.glb";

        // Graceful path: the remote editor imports the glbs on next rescan;
        // until then skip the prop instead of killing the arena build.
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"GRAVEYARD: {path} not imported yet — prop skipped");
            return;
        }

        Node? model;
        try
        {
            model = GD.Load<PackedScene>(path).Instantiate();
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"GRAVEYARD: {path} failed to load — {e.Message}");
            return;
        }
        if (model is null)
        {
            GD.PushWarning($"GRAVEYARD: {path} instantiated null — prop skipped");
            return;
        }

        var body = new StaticBody3D { Name = $"Grave_{shortName}_{rng.RandiRange(0, 9999)}" };
        body.AddChild(model);

        // Log the mesh Aabb once on first load so extents can be tuned
        // against the 1.2-1.6 m slab-height target.
        if (model is MeshInstance3D meshRoot && meshRoot.Mesh is not null)
            GD.Print($"GRAVEYARD Aabb {shortName}: pos={meshRoot.Mesh.GetAabb().Position} " +
                     $"size={meshRoot.Mesh.GetAabb().Size}");

        if (collide)
        {
            var aabb = GetTreeAabb(model);
            if (aabb.Size.Length() > 0.01f)
            {
                var cs = new CollisionShape3D
                {
                    Shape = new BoxShape3D { Size = aabb.Size * scale },
                    Position = (aabb.Position + aabb.Size / 2f) * scale,
                };
                body.AddChild(cs);
            }
        }

        body.Position = new Vector3(x, 0f, z);
        body.RotationDegrees = new Vector3(
            (float)rng.RandfRange(-tiltDeg, tiltDeg) * 0.35f,
            yawDeg + (float)rng.RandfRange(-8f, 8f),
            (float)rng.RandfRange(-tiltDeg, tiltDeg));
        body.Scale = new Vector3(1, 1, 1) * scale;
        AddChild(body);
    }

    /// <summary>Combined node-local Aabb of every MeshInstance3D under the
    /// instantiated glb (the glb root is a Node3D, not the mesh itself).</summary>
    private static Aabb GetTreeAabb(Node root)
    {
        var bounds = new Aabb();
        bool first = true;
        Collect(root);
        return bounds;

        void Collect(Node node)
        {
            if (node is MeshInstance3D mi && mi.Mesh is not null)
            {
                var local = mi.Mesh.GetAabb();
                // Mesh-local -> node-local via the mesh node transform.
                var box = new Aabb(mi.Transform * local.Position, Vector3.Zero)
                    .Expand(mi.Transform * local.End);
                if (first)
                    bounds = box;
                else
                    bounds = bounds.Merge(box);
                first = false;
            }
            foreach (var child in node.GetChildren())
                Collect(child);
        }
    }
}
