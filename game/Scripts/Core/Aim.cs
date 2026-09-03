using Godot;

namespace Hollowcrown.Core;

/// <summary>
/// Cursor -> ground point. Universal targeting (Vision 6.12): every skill
/// aims at the ground plane (y=0) under the mouse cursor.
/// </summary>
public static class Aim
{
    public static Vector3 CursorGroundPoint(Camera3D cam)
    {
        var mouse = cam.GetViewport().GetMousePosition();
        var from = cam.ProjectRayOrigin(mouse);
        var dir = cam.ProjectRayNormal(mouse);
        return new Plane(Vector3.Up, 0f).IntersectsRay(from, dir) ?? cam.GlobalPosition;
    }
}
