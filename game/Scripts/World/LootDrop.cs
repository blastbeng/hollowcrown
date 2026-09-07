using Godot;
using Hollowcrown.Save;

namespace Hollowcrown.World;

/// <summary>
/// A loot drop on the arena floor (Vision 8): the AUTHORITY rolls the item
/// (seeded, deterministic), broadcasts a drop with the rolled seed + item
/// level, and every peer rebuilds the SAME item locally from the seed.
/// Pickup is a proximity check driven by the local player; the server
/// validates who was actually close enough before awarding.
/// </summary>
public partial class LootDrop : Node3D
{
    public const float PickupRadius = 1.6f;
    public const float Lifetime = 45f;

    public int DropId { get; set; }
    public ulong Seed { get; set; }
    public int ItemLevel { get; set; } = 1;

    private static readonly Color[] RarityGlow =
    {
        new("#9aa0a8"),   // common — cold steel
        new("#4a8a3a"),   // uncommon — moss
        new("#3a6ad0"),   // rare — deep blue
        new("#8a4ad0"),   // epic — violet
        new("#e0a03c"),   // mythic — ember gold
    };

    private ItemGenerator.Item? _item;
    private bool _taken;
    private float _age;

    public override void _Ready()
    {
        // Deterministic rebuild: the same seed on every peer produces the
        // same item (server drops; clients mirror the visual).
        var rng = new RandomNumberGenerator { Seed = Seed };
        _item = ItemGenerator.Generate(rng, ItemLevel);

        var rarityIndex = (int)_item.Rarity;
        var glow = new OmniLight3D
        {
            LightColor = RarityGlow[rarityIndex],
            LightEnergy = 1.6f,
            OmniRange = 2.6f,
            ShadowEnabled = false,
            Position = new Vector3(0, 0.5f, 0),
        };
        AddChild(glow);

        var shard = new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(0.28f, 0.42f, 0.28f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = RarityGlow[rarityIndex],
                EmissionEnabled = true,
                Emission = RarityGlow[rarityIndex],
                EmissionEnergyMultiplier = 1.8f,
                Roughness = 0.4f,
            },
            Position = new Vector3(0, 0.3f, 0),
        };
        AddChild(shard);

        // Slow spin so drops read as interactive at iso zoom (Vision 6.9).
        GD.Print($"LOOT DROP READY — id={DropId} {_item.Name} ({ItemGenerator.RarityLabel(_item.Rarity)}, ilvl {_item.ItemLevel})");
    }

    public ItemGenerator.Item? Item => _item;
    public bool IsTaken => _taken;

    public void MarkTaken() => _taken = true;

    public override void _Process(double delta)
    {
        _age += (float)delta;
        RotateY(Mathf.DegToRad(90f * (float)delta));
        if (_age > Lifetime)
            QueueFree();
    }
}