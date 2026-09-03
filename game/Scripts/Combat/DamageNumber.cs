using Godot;

namespace Hollowcrown.Combat;

/// <summary>
/// Floating combat text (Vision 6.9): bone-colored numbers that rise ~0.8 m
/// and fade over 0.7 s. The heavy chain finisher gets the accent gold color
/// and a larger size so the combo rhythm reads at isometric zoom.
/// </summary>
public partial class DamageNumber : Label3D
{
    private const float Life = 0.7f;
    private const float RiseSpeed = 1.2f;
    private float _age;
    private Color _baseColor;

    public static void Spawn(Node3D parent, Vector3 worldPos, int amount, bool heavy = false)
    {
        var n = new DamageNumber
        {
            Text = amount.ToString(),
            FontSize = heavy ? 64 : 44,
            OutlineSize = 10,
            PixelSize = 0.004f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = false,
        };
        n._baseColor = heavy
            ? Color.FromHtml("b08d57")     // UI accent / gold (Vision 6.10)
            : Color.FromHtml("d8cfc0");    // bone (Vision 6.10)
        n.Modulate = n._baseColor;
        n.Position = parent.ToLocal(worldPos + new Vector3(0f, 1.6f, 0f));
        parent.AddChild(n);
    }

    public override void _Process(double deltaRaw)
    {
        float delta = (float)deltaRaw;
        _age += delta;
        Position += Vector3.Up * RiseSpeed * delta;
        float a = 1f - _age / Life;
        if (a <= 0f)
        {
            QueueFree();
            return;
        }
        var c = _baseColor;
        c.A = a;
        Modulate = c;
    }
}
