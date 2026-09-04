using System;
using System.Collections.Generic;

namespace Hollowcrown.Combat;

/// <summary>One HUD ability-bar slot (Vision 6.10 isometric convention):
/// key cap, name, cooldown fraction (0..1 drives the sweep) and an "active"
/// glow predicate. Each class kit provides its own slots so ArenaHud stays
/// class-agnostic.</summary>
public readonly struct AbilitySlot
{
    public AbilitySlot(string key, string name, Func<float> cdFraction, Func<bool> active)
    {
        Key = key;
        Name = name;
        CdFraction = cdFraction;
        Active = active;
    }

    public string Key { get; }
    public string Name { get; }
    public Func<float> CdFraction { get; }
    public Func<bool> Active { get; }
}

/// <summary>Implemented by every class chain/kit node; the HUD walks the
/// player's children collecting these.</summary>
public interface IAbilityProvider
{
    IEnumerable<AbilitySlot> Slots();
}
