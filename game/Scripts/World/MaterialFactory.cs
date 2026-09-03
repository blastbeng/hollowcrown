using Godot;

namespace Hollowcrown.World;

/// <summary>
/// Cached procedural materials (Vision 6.3): every visible surface gets a
/// StandardMaterial3D with an albedo texture (procedural NoiseTexture2D until
/// store texture packs are installed); AlbedoColor tints the texture toward
/// the palette (Vision 6.10). Nothing visible keeps a default material.
/// </summary>
public static class MaterialFactory
{
    private static readonly System.Collections.Generic.Dictionary<string, StandardMaterial3D> Cache = new();

    public static StandardMaterial3D Get(string key, Color tint, float roughness = 0.85f,
        float metallic = 0f, bool occluder = false, float uvScale = 1f)
    {
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var noise = new FastNoiseLite { Frequency = 0.08f, FractalOctaves = 3 };
        var tex = new NoiseTexture2D { Noise = noise, Width = 256, Height = 256, Seamless = true };
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = tex,
            AlbedoColor = tint,               // multiplies the texture
            Roughness = roughness,            // 0.6-0.95 per Vision 6.3
            Metallic = metallic,
            Uv1Scale = new Vector3(uvScale, uvScale, uvScale),
        };
        if (occluder)
        {
            // Per-instance transparency (GeometryInstance3D.Transparency) only
            // applies when the material's transparency is not disabled.
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }
        Cache[key] = mat;
        return mat;
    }

    // Palette helpers (Vision 6.10). Floor darker than walls so paths and
    // silhouettes read against the ground (Vision 6.6).
    public static StandardMaterial3D FloorStone() => Get("floor_stone", Color.FromHtml("55555a"), 0.92f, uvScale: 10f);
    public static StandardMaterial3D WallStone() => Get("wall_stone", Color.FromHtml("5a5a5e"), 0.85f, occluder: true, uvScale: 3f);
    public static StandardMaterial3D PlayerSteel() => Get("player_steel", Color.FromHtml("8a919c"), 0.45f, metallic: 0.5f, uvScale: 2f);
}
