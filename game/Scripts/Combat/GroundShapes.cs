using Godot;

namespace Hollowcrown.Combat;

/// <summary>
/// Builds flat GROUND-PROJECTED combat shapes (Vision 6.9). All geometry
/// lives in the XZ plane at y = 0; callers lift meshes ~3 cm above the floor
/// to avoid z-fighting. Shared by the Warden chain, kit, and future class
/// abilities so every hitbox flash IS the hitbox shape.
/// </summary>
public static class GroundShapes
{
    /// <summary>Flat sector (pie slice) centered on origin, opening toward
    /// <paramref name="facing"/>. Used for arcs (wide) and cones (narrow).</summary>
    public static ArrayMesh Sector(float radius, float arcDeg, Vector3 facing)
    {
        const int segments = 14;
        float arcRad = Mathf.DegToRad(arcDeg);
        float half = arcRad * 0.5f;
        float baseYaw = Mathf.Atan2(-facing.X, -facing.Z);   // body-style yaw
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < segments; i++)
        {
            float a0 = -half + arcRad * i / segments;
            float a1 = -half + arcRad * (i + 1) / segments;
            st.SetNormal(Vector3.Up);
            st.AddVertex(Vector3.Zero);
            st.SetNormal(Vector3.Up);
            st.AddVertex(PointAt(baseYaw + a0, radius));
            st.SetNormal(Vector3.Up);
            st.AddVertex(PointAt(baseYaw + a1, radius));
        }
        return st.Commit();
    }

    /// <summary>Flat rectangle strip from origin toward <paramref name="facing"/>
    /// (length x width). Used for line hitboxes (nightblade smoke trail,
    /// revenant bone spear) and VFX flashes.</summary>
    public static ArrayMesh Line(float length, float width, Vector3 facing)
    {
        float baseYaw = Mathf.Atan2(-facing.X, -facing.Z);   // body-style yaw
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var halfW = new Vector3(width * 0.5f, 0f, 0f);
        // Local frame: -Z forward, +X right, then rotated by baseYaw.
        var fwd = PointAt(baseYaw, length);
        var right = new Vector3(Mathf.Cos(baseYaw), 0f, -Mathf.Sin(baseYaw));
        var a = right * -halfW.Length();
        var b = right * halfW.Length();
        st.SetNormal(Vector3.Up); st.AddVertex(a);
        st.SetNormal(Vector3.Up); st.AddVertex(fwd + b);
        st.SetNormal(Vector3.Up); st.AddVertex(fwd + a);
        st.SetNormal(Vector3.Up); st.AddVertex(a);
        st.SetNormal(Vector3.Up); st.AddVertex(b);
        st.SetNormal(Vector3.Up); st.AddVertex(fwd + b);
        return st.Commit();
    }

    private static Vector3 PointAt(float yaw, float radius)
        => new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw)) * radius;   // forward = -Z
}
