using Godot;

namespace Hollowcrown.World;

/// <summary>
/// Atmosphere particles (Vision 6.2 / 6.7): rising ember motes near brazier
/// lights and light rain over the arena. GPUParticles3D, one-shot curves,
/// palette colors (ember #e08a3c, cold rain). Deterministic seeds (Vision 6).
/// </summary>
public static class AtmosphereParticles
{
    /// <summary>Ember motes drifting up from a brazier bowl (attach at the
    /// bowl position; local space keeps them with the brazier).</summary>
    public static GpuParticles3D Embers()
    {
        var mat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.25f,
            Direction = new Vector3(0, 1, 0),
            Spread = 12f,
            InitialVelocityMin = 0.6f,
            InitialVelocityMax = 1.4f,
            Gravity = new Vector3(0, 0.4f, 0),          // embers RISE (heat)
            ScaleMin = 0.03f,
            ScaleMax = 0.08f,
            Color = Color.FromHtml("e08a3c"),           // ember (Vision 6.10)
            DampingMin = 0.5f,
            DampingMax = 1.5f,
        };
        // Fade out over life so motes wink like sparks.
        var alpha = new Gradient();
        alpha.AddPoint(0.0f, new Color(1f, 1f, 1f, 1f));
        alpha.AddPoint(0.7f, new Color(1f, 1f, 1f, 0.8f));
        alpha.AddPoint(1.0f, new Color(1f, 1f, 1f, 0f));
        mat.ColorRamp = new GradientTexture1D { Gradient = alpha };

        var particles = new GpuParticles3D
        {
            Amount = 24,
            Lifetime = 1.8f,
            ProcessMaterial = mat,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.07f, 0.07f) },
            LocalCoords = true,
        };
        particles.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
        };
        return particles;
    }

    /// <summary>Light rain over a rectangular area (attach high above the
    /// arena, local space; velocity carries drops down).</summary>
    public static GpuParticles3D Rain(Vector2 area)
    {
        var mat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(area.X * 0.5f, 0.1f, area.Y * 0.5f),
            Direction = new Vector3(0.12f, -1f, 0),     // slight wind slant
            Spread = 2f,
            InitialVelocityMin = 14f,
            InitialVelocityMax = 18f,
            Gravity = new Vector3(0, -2f, 0),
            ScaleMin = 0.8f,
            ScaleMax = 1.2f,
            Color = new Color(0.62f, 0.66f, 0.74f, 0.28f),   // cold, faint
        };

        var particles = new GpuParticles3D
        {
            Amount = 340,
            Lifetime = 1.4f,
            ProcessMaterial = mat,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.02f, 0.45f) },   // streaks
            LocalCoords = true,
        };
        particles.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return particles;
    }
}
