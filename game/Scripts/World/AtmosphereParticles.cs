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
    /// bowl position; local space keeps them with the brazier). Tuned for
    /// DISTINCT RISING SPARKS (Vision 6.7 session note): faster burst, air
    /// drag, turbulence wobble, bright-to-ember ramp with a hard alpha wink
    /// — reads as sparks, not a static glow blob.</summary>
    public static GpuParticles3D Embers()
    {
        var mat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.2f,
            Direction = new Vector3(0, 1, 0),
            Spread = 16f,
            InitialVelocityMin = 1.6f,
            InitialVelocityMax = 2.8f,          // visible spark burst
            Gravity = new Vector3(0, 0.55f, 0), // embers RISE (heat)
            ScaleMin = 0.02f,
            ScaleMax = 0.06f,                   // smaller = spark, not glow
            Color = Color.FromHtml("ffb14d"),   // hotter core than #e08a3c
            DampingMin = 0.6f,
            DampingMax = 1.4f,
            TurbulenceEnabled = true,
            TurbulenceNoiseScale = 2.2f,
            TurbulenceInfluence = new Vector2(0.12f, 0.12f),   // wander, not a straight line
        };
        // Spark life curve: bright birth, flicker mid-air, hard wink out.
        var alpha = new Gradient();
        alpha.AddPoint(0.0f, new Color(1f, 0.95f, 0.8f, 1f));
        alpha.AddPoint(0.35f, new Color(1f, 0.75f, 0.4f, 0.9f));
        alpha.AddPoint(0.7f, new Color(0.95f, 0.5f, 0.2f, 0.45f));
        alpha.AddPoint(1.0f, new Color(0.9f, 0.4f, 0.15f, 0f));
        mat.ColorRamp = new GradientTexture1D { Gradient = alpha };
        // Size shrinks over life so sparks die out, not fade into haze.
        var scaleCurve = new Gradient();
        scaleCurve.AddPoint(0.0f, new Color(1f, 1f, 1f));
        scaleCurve.AddPoint(0.75f, new Color(0.7f, 0.7f, 0.7f));
        scaleCurve.AddPoint(1.0f, new Color(0f, 0f, 0f));
        mat.ScaleCurve = new GradientTexture1D { Gradient = scaleCurve };

        var particles = new GpuParticles3D
        {
            Amount = 30,
            Lifetime = 1.4f,
            ProcessMaterial = mat,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.06f, 0.06f) },
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
