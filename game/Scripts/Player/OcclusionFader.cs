using Godot;

namespace Hollowcrown.Player;

/// <summary>
/// Occlusion fade (Vision 6.1, MANDATORY): every physics frame, cast a ray
/// from the camera to the player; any mesh of a body in the "occluder" group
/// hit by the ray fades to ~0.2 alpha and restores when clear. Never let
/// walls hide the player.
/// </summary>
public partial class OcclusionFader : Node
{
    [Export] public NodePath TargetPath = default!;
    [Export] public float OccludedAlpha = 0.2f;
    [Export] public float Speed = 10f;

    private Node3D _target = null!;
    private readonly System.Collections.Generic.Dictionary<MeshInstance3D, StaticBody3D> _occluders = new();

    public override void _Ready()
    {
        _target = GetNode<Node3D>(TargetPath);
        foreach (var node in GetTree().GetNodesInGroup("occluder"))
        {
            if (node is not StaticBody3D body)
                continue;
            foreach (var child in body.FindChildren("*", "MeshInstance3D", true, false))
                if (child is MeshInstance3D mesh)
                    _occluders[mesh] = body;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam is null)
            return;

        var from = cam.GlobalPosition;
        var to = _target.GlobalPosition + Vector3.Up * 1.2f; // chest height
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        // Never hit the followed body itself (the ray ends inside its
        // collider; Godot 4.7 Variant.As<T> throws on the mismatch).
        if (_target is CollisionObject3D self)
            query.Exclude = new Godot.Collections.Array<Rid> { self.GetRid() };
        var hit = _target.GetWorld3D().DirectSpaceState.IntersectRay(query);

        MeshInstance3D? hitMesh = null;
        if (hit.Count > 0 && hit.TryGetValue("collider", out var collider)
            && collider.Obj is StaticBody3D body)
        {
            foreach (var kv in _occluders)
            {
                if (kv.Value == body)
                {
                    hitMesh = kv.Key;
                    break;
                }
            }
        }

        float goalTransparency = 1f - OccludedAlpha;  // 0.8 => 0.2 visible alpha
        float k = 1f - Mathf.Exp(-Speed * (float)delta);
        foreach (var kv in _occluders)
        {
            float goal = kv.Key == hitMesh ? goalTransparency : 0f;
            kv.Key.Transparency = Mathf.Lerp(kv.Key.Transparency, goal, k);
        }
    }
}
