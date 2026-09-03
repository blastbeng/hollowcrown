using Godot;

namespace Hollowcrown.Player;

/// <summary>
/// Isometric camera rig (Vision 6.1): yaw LOCKED at 45 deg, child Camera3D
/// pitched -50 deg, orthogonal projection (zoom 8-18 via mouse wheel), smooth
/// follow of the target, tight far plane. Never free-rotates.
/// </summary>
public partial class IsoCameraRig : Node3D
{
    [Export] public NodePath TargetPath = default!;
    [Export] public float ZoomStep = 1f, MinZoom = 8f, MaxZoom = 18f;

    private Node3D _target = null!;
    private Camera3D _cam = null!;

    public override void _Ready()
    {
        _target = GetNode<Node3D>(TargetPath);
        _cam = GetNodeOrNull<Camera3D>("Camera3D");
        if (_cam is null)                              // rig is self-sufficient
        {
            _cam = new Camera3D { Name = "Camera3D" };
            AddChild(_cam);
        }
        RotationDegrees = new Vector3(0f, 45f, 0f);    // yaw locked
        _cam.Projection = Camera3D.ProjectionType.Orthogonal;
        _cam.Size = 12f;
        _cam.Far = 60f;
        _cam.Position = new Vector3(0f, 18f, 18f);         // above/behind
        _cam.RotationDegrees = new Vector3(-50f, 0f, 0f);  // pitch down
        _cam.Current = true;
    }

    public override void _Process(double delta)
    {
        GlobalPosition = GlobalPosition.Lerp(_target.GlobalPosition,
            1f - Mathf.Exp(-10f * (float)delta));          // smooth follow
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
                _cam.Size = Mathf.Max(MinZoom, _cam.Size - ZoomStep);
            else if (mb.ButtonIndex == MouseButton.WheelDown)
                _cam.Size = Mathf.Min(MaxZoom, _cam.Size + ZoomStep);
        }
    }
}
