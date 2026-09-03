using Godot;
using Hollowcrown.Core;

namespace Hollowcrown.Player;

/// <summary>
/// Cursor reticle (Vision 6.1): a flat accent ring on the ground that follows
/// the aim point under the mouse cursor every frame.
/// </summary>
public partial class Reticle : Node3D
{
    private Camera3D _cam = null!;

    public override void _Ready()
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Color.FromHtml("b08d57"),      // UI accent / gold
            EmissionEnabled = true,
            Emission = Color.FromHtml("b08d57"),
        };
        var ring = new MeshInstance3D
        {
            Name = "Ring",
            Mesh = new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.5f },
            MaterialOverride = mat,
        };
        AddChild(ring);
    }

    public override void _Process(double delta)
    {
        _cam ??= GetViewport().GetCamera3D();
        if (_cam is null)
            return;
        var p = Aim.CursorGroundPoint(_cam);
        GlobalPosition = new Vector3(p.X, 0.02f, p.Z);   // flat on the ground
    }
}
